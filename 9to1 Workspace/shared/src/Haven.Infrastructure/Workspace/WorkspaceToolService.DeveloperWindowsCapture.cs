using System.Security.Cryptography;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    private sealed partial class DeveloperCaptureSource
    {
        private async Task VisitWindows(PhysicalSelection original, IDeveloperProjectOriginalReadSelection logical,
            IDeveloperProjectOriginalReadAdmission admission, List<string> folders, List<DeveloperProjectCapturedSourceFile> files,
            List<HeldFile> held, CancellationToken token)
        {
            long total = 0;
            await Visit(original.Project, original.OriginalProjectRoot, "", 0).ConfigureAwait(false);
            async Task Visit(SafeFileHandle retainedParent, string path, string relative, int depth)
            {
                token.ThrowIfCancellationRequested(); if (depth > 10) throw new InvalidOperationException("Original source folder depth exceeds 10; incomplete capture refused.");
                await Observe(original, () => admissions.ValidateOriginalAsync(logical, admission, token)).ConfigureAwait(false);
                var before = Invoke(() => admission.RunOriginalRead(() => ReadDeveloperWindowsIdentity(retainedParent), token));
                Invoke(() => admission.RunOriginalRead(() => { DemandDeveloperWindowsPath(retainedParent, path, true); DemandDeveloperWindowsCase(retainedParent); return true; }, token));
                var names = new List<string>(); var restart = true;
                while (true)
                {
                    await Observe(original, () => admissions.ValidateOriginalAsync(logical, admission, token)).ConfigureAwait(false);
                    var complete = false;
                    var batch = Invoke(() => admission.RunOriginalRead(() => ReadDeveloperWindowsDirectoryBatch(retainedParent, restart, out complete), token));
                    restart = false;
                    foreach (var name in batch)
                    {
                        if (names.Count >= 128) throw new InvalidOperationException("Original directory scan exceeds the bounded source limit; incomplete capture refused.");
                        if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("The original native directory returned an ambiguous/repeated entry.");
                        names.Add(name);
                    }
                    if (complete) break;
                }
                foreach (var name in names.Order(StringComparer.Ordinal))
                {
                    var child = Path.Combine(path, name); var childRelative = relative.Length == 0 ? name : relative + "/" + name;
                    SafeFileHandle? candidate = null; FileStream? stream = null; var errors = new List<Exception>();
                    try
                    {
                        await Observe(original, () => admissions.ValidateOriginalAsync(logical, admission, token)).ConfigureAwait(false);
                        // Attribute-only first open observes kind without reading content or
                        // opening a pipe/device. Native child stays rooted to this SAME parent.
                        candidate = Invoke(() => admission.RunOriginalRead(() => OpenDeveloperWindowsAt(retainedParent, name,
                            DeveloperWindowsReadControl | DeveloperWindowsSynchronize | DeveloperWindowsAttributes, 1, 1, null), token));
                        var identity = Invoke(() => admission.RunOriginalRead(() => ReadDeveloperWindowsIdentity(candidate), token));
                        Invoke(() => admission.RunOriginalRead(() => { DemandDeveloperWindowsPath(candidate, child, identity.IsDirectory); return true; }, token));
                        if (identity.Volume != before.Volume) throw new UnauthorizedAccessException("The original Windows source crosses its retained volume.");
                        if (identity.IsDirectory)
                        {
                            if (folders.Count >= DeveloperProjectSetupIntent.MaximumFolders) throw new InvalidOperationException("Original source folder count exceeded; incomplete capture refused.");
                            candidate.Dispose(); candidate = null;
                            candidate = Invoke(() => admission.RunOriginalRead(() => OpenDeveloperWindowsAt(retainedParent, name, DeveloperWindowsDirectoryAccess, 3, 1, true), token));
                            if (!Invoke(() => admission.RunOriginalRead(() => ReadDeveloperWindowsIdentity(candidate), token)).SameReadVersion(identity)) throw new IOException("The original Windows source directory changed before traversal.");
                            var directory = candidate;
                            if (Invoke(() => admission.RunOriginalRead(() => original.WindowsRoot!.RetainCapturedDirectory(child, directory, identity), token))) candidate = null;
                            folders.Add(childRelative); await Observe(original, () => Visit(directory, child, childRelative, depth + 1)).ConfigureAwait(false);
                        }
                        else
                        {
                            if (!identity.IsRegular || identity.Links != 1) throw new UnauthorizedAccessException("Original source entry is not an unaliased regular file.");
                            if (files.Count >= DeveloperProjectSetupIntent.MaximumFiles || identity.Size > (ulong)DeveloperProjectSetupIntent.MaximumFileBytes)
                                throw new InvalidOperationException("Original source file limit exceeded; incomplete capture refused.");
                            candidate.Dispose(); candidate = null;
                            candidate = Invoke(() => admission.RunOriginalRead(() => OpenDeveloperWindowsAt(retainedParent, name,
                                DeveloperWindowsReadControl | DeveloperWindowsSynchronize | DeveloperWindowsAttributes | DeveloperWindowsRead, 1, 1, false), token));
                            var readIdentity = Invoke(() => admission.RunOriginalRead(() => ReadDeveloperWindowsIdentity(candidate), token));
                            if (!readIdentity.SameReadVersion(identity)) throw new IOException("Original Windows source changed before its content read.");
                            stream = Invoke(() => new FileStream(candidate, FileAccess.Read, 4096, isAsync: false));
                            held.Add(new(childRelative, stream, candidate, default, readIdentity));
                            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[16384]; long bytes = 0;
                            while (true)
                            {
                                await Observe(original, () => admissions.ValidateOriginalAsync(logical, admission, token)).ConfigureAwait(false);
                                var count = await ObserveRead(original, admission, () => stream.ReadAsync(buffer.AsMemory(), token).AsTask(), token).ConfigureAwait(false);
                                if (count == 0) break; bytes += count; total += count;
                                if (bytes > DeveloperProjectSetupIntent.MaximumFileBytes || total > DeveloperProjectSetupIntent.MaximumTotalBytes)
                                    throw new InvalidOperationException("Original source byte limit exceeded; incomplete capture refused.");
                                hash.AppendData(buffer, 0, count);
                            }
                            var after = Invoke(() => admission.RunOriginalRead(() => ReadDeveloperWindowsIdentity(candidate), token));
                            Invoke(() => admission.RunOriginalRead(() => { DemandCurrent(original); DemandDeveloperWindowsPath(candidate, child, false); return true; }, token));
                            if (!readIdentity.SameReadVersion(after) || (ulong)bytes != after.Size) throw new IOException("Original Windows source file changed during capture.");
                            files.Add(new(childRelative, bytes, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), "dev-source-file:" + Guid.NewGuid().ToString("D")));
                            stream = null; candidate = null; // SAME original capture owns these until whole drain.
                        }
                    }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    finally
                    {
                        if (stream is not null && !held.Any(value => ReferenceEquals(value.OriginalStream, stream)))
                            try { stream.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                        if (candidate is not null && !held.Any(value => ReferenceEquals(value.OriginalHandle, candidate)))
                            try { candidate.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    }
                    if (errors.Count != 0 && errors.All(error => error is OperationCanceledException))
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
                    ThrowOriginalErrors(errors);
                }
                Invoke(() => admission.RunOriginalRead(() => { DemandCurrent(original); DemandDeveloperWindowsPath(retainedParent, path, true); return true; }, token));
                if (!Invoke(() => admission.RunOriginalRead(() => ReadDeveloperWindowsIdentity(retainedParent), token)).SameReadVersion(before))
                    throw new IOException("Original Windows source directory changed during enumeration.");
            }
        }
        private static void CaptureWindowsDirectory(DirectoryPreparation preparation)
        {
            var physical = preparation.Capture.Physical;
            preparation.Handle = physical.WindowsRoot!.OpenDirectory(preparation.DirectoryPath);
            preparation.WindowsIdentity = ReadDeveloperWindowsIdentity(preparation.Handle);
            DemandDeveloperWindowsPath(preparation.Handle, preparation.DirectoryPath, true);
            if (!preparation.WindowsIdentity.Value.IsDirectory) throw new UnauthorizedAccessException("The actual captured Windows path is not a directory.");
        }
        private static void DemandWindowsDirectoryVersion(DirectoryPreparation preparation, SafeFileHandle handle)
        {
            DemandDeveloperWindowsPath(handle, preparation.DirectoryPath, true); DemandDeveloperWindowsCase(handle);
            if (preparation.WindowsIdentity is not { } identity || !ReadDeveloperWindowsIdentity(handle).SameReadVersion(identity))
                throw new IOException("The original Windows directory no longer matches its retained physical identity/version.");
        }
        private static void DemandWindowsFileVersion(FileRegistrationPreparation preparation)
        {
            DemandDeveloperWindowsPath(preparation.Handle!, preparation.OriginalFilePath, false);
            if (preparation.WindowsIdentity is not { } identity || !ReadDeveloperWindowsIdentity(preparation.Handle!).SameReadVersion(identity))
                throw new IOException("The exact original Windows source changed before metadata publication.");
        }
    }
}
