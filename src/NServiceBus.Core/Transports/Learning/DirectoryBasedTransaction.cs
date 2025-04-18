namespace NServiceBus;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Logging;

class DirectoryBasedTransaction : ILearningTransportTransaction
{
    public DirectoryBasedTransaction(string basePath, string pendingDirName, string committedDirName, string transactionId)
    {
        this.basePath = basePath;

        transactionDir = Path.Combine(basePath, pendingDirName, transactionId);
        commitDir = Path.Combine(basePath, committedDirName, transactionId);
    }

    public string FileToProcess { get; private set; }

    public async Task<bool> BeginTransaction(string incomingFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(transactionDir);
        FileToProcess = Path.Combine(transactionDir, Path.GetFileName(incomingFilePath));

        var succeeded = await AsyncFile.Move(incomingFilePath, FileToProcess, cancellationToken).ConfigureAwait(false);
        if (succeeded)
        {
            return true;
        }

        Directory.Delete(transactionDir, true);
        return false;
    }

    public async Task Commit(CancellationToken cancellationToken = default)
    {
        await AsyncDirectory.Move(transactionDir, commitDir, cancellationToken).ConfigureAwait(false);
        committed = true;
    }

    public void Rollback()
    {
        //rollback by moving the file back to the main dir
        File.Move(FileToProcess, Path.Combine(basePath, Path.GetFileName(FileToProcess)));
        Directory.Delete(transactionDir, true);
    }

    public void ClearPendingOutgoingOperations()
    {
        while (outgoingFiles.TryDequeue(out _))
        { }
    }

    public Task Enlist(string messagePath, string messageContents, CancellationToken cancellationToken = default)
    {
        var inProgressFileName = Path.GetFileNameWithoutExtension(messagePath) + ".out";

        var txPath = Path.Combine(transactionDir, inProgressFileName);
        var committedPath = Path.Combine(commitDir, inProgressFileName);

        outgoingFiles.Enqueue(new OutgoingFile(committedPath, messagePath));

        return AsyncFile.WriteText(txPath, messageContents, cancellationToken);
    }

    public bool Complete()
    {
        if (!committed)
        {
            return false;
        }

        while (outgoingFiles.TryDequeue(out var outgoingFile))
        {
            File.Move(outgoingFile.TxPath, outgoingFile.TargetPath);
        }

        Directory.Delete(commitDir, true);

        return true;
    }

    public static void RecoverPartiallyCompletedTransactions(string basePath, string pendingDirName, string committedDirName)
    {
        var pendingRootDir = Path.Combine(basePath, pendingDirName);

        if (Directory.Exists(pendingRootDir))
        {
            foreach (var transactionDir in new DirectoryInfo(pendingRootDir).EnumerateDirectories())
            {
                new DirectoryBasedTransaction(basePath, pendingDirName, committedDirName, transactionDir.Name)
                    .RecoverPending();
            }
        }

        var committedRootDir = Path.Combine(basePath, committedDirName);

        if (Directory.Exists(committedRootDir))
        {
            foreach (var transactionDir in new DirectoryInfo(committedRootDir).EnumerateDirectories())
            {
                new DirectoryBasedTransaction(basePath, pendingDirName, committedDirName, transactionDir.Name)
                    .RecoverCommitted();
            }
        }
    }

    void RecoverPending()
    {
        var pendingDir = new DirectoryInfo(transactionDir);

        try
        {
            //only need to move the incoming file
            foreach (var file in pendingDir.EnumerateFiles(TxtFileExtension))
            {
                var destFileName = Path.Combine(basePath, file.Name);
                try
                {
                    File.Move(file.FullName, destFileName);
                }
                catch (Exception e)
                {
                    log.Debug($"Unable to move pending transaction from '{file.FullName}' to '{destFileName}'. Pending transaction is assumed to be recovered by a competing consumer.", e);
                }
            }


            pendingDir.Delete(true);
        }
        catch (Exception e)
        {
            log.Debug($"Unable to recover pending transaction '{pendingDir.FullName}'.", e);
        }
    }

    void RecoverCommitted()
    {
        var committedDir = new DirectoryInfo(commitDir);

        try
        {
            //for now just rollback the completed ones as well. We could consider making this smarter in the future
            // but its good enough for now since duplicates is a possibility anyway
            foreach (var file in committedDir.EnumerateFiles(TxtFileExtension))
            {
                var destFileName = Path.Combine(basePath, file.Name);
                try
                {
                    File.Move(file.FullName, destFileName);
                }
                catch (Exception e)
                {
                    log.Debug($"Unable to move committed transaction from '{file.FullName}' to '{destFileName}'. Committed transaction is assumed to be recovered by a competing consumer.", e);
                }
            }

            committedDir.Delete(true);
        }
        catch (Exception e)
        {
            log.Debug($"Unable to recover committed transaction '{committedDir.FullName}'.", e);
        }
    }

    readonly string basePath;
    readonly string commitDir;

    bool committed;

    readonly ConcurrentQueue<OutgoingFile> outgoingFiles = new ConcurrentQueue<OutgoingFile>();
    readonly string transactionDir;

    const string TxtFileExtension = "*.txt";

    static readonly ILog log = LogManager.GetLogger<DirectoryBasedTransaction>();

    class OutgoingFile
    {
        public OutgoingFile(string txPath, string targetPath)
        {
            TxPath = txPath;
            TargetPath = targetPath;
        }

        public string TxPath { get; }
        public string TargetPath { get; }
    }
}