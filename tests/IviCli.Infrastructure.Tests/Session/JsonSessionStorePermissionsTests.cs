using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using IviCli.Domain.Session;
using IviCli.Infrastructure.Session;
using IviCli.TestKit;

namespace IviCli.Infrastructure.Tests.Session;

/// <summary>
/// The session file records which instrument each alias points at and which
/// scenario answers it, so it is locked down to the account that wrote it
/// (ADR 0017 §4). These run against the real file system — permissions are
/// the one thing <c>MockFileSystem</c> cannot stand in for.
/// </summary>
public class JsonSessionStorePermissionsTests
{
    [Fact]
    public async Task SaveAsync_LeavesTheSessionReadableOnlyByItsOwner()
    {
        // Given
        var fs = new FileSystem();
        var directory = fs.Path.Combine(
            fs.Path.GetTempPath(),
            "ivi-session-acl-" + Guid.NewGuid().ToString("N")
        );
        fs.Directory.CreateDirectory(directory);
        try
        {
            var path = fs.Path.Combine(directory, "session.json");
            var store = new JsonSessionStore(fs, path);

            // When
            (await store.SaveAsync(SessionState.Empty, CancellationToken.None)).ShouldBeOk();

            // Then
            if (OperatingSystem.IsWindows())
            {
                AssertOnlyTheCurrentUserIsGranted(path);
            }
            else
            {
                File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            fs.Directory.Delete(directory, recursive: true);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AssertOnlyTheCurrentUserIsGranted(string path)
    {
        var security = new FileInfo(path).GetAccessControl();
        security.AreAccessRulesProtected.ShouldBeTrue("the profile's grants must not be inherited");

        var granted = security
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier)
            )
            .Cast<FileSystemAccessRule>()
            .ToList();
        var user = WindowsIdentity.GetCurrent().User;

        granted.ShouldAllBe(rule => rule.IdentityReference == user);
        granted.ShouldContain(rule =>
            rule.AccessControlType == AccessControlType.Allow
            && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)
        );
    }
}
