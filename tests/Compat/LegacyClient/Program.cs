using System;
using System.IO;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

// Usage: LegacyClient <releases-dir> <channel> <installed-version> <expected-version> <root-dir> <app-dir> <update-exe> <packages-dir>
//
// Uses Velopack 0.0.1298 - the library inside Hash Tool 1.1.x - to read the new release feed, pick the update and
// download it with its checksum checks, exactly as an installed 1.1.x copy would. Prints the downloaded package path;
// the release workflow then lets the old updater apply it and smoke-tests the result.
if (args.Length != 8)
{
    Console.Error.WriteLine("usage: LegacyClient <releases-dir> <channel> <installed-version> <expected-version> <root-dir> <app-dir> <update-exe> <packages-dir>");
    return 2;
}

var (releases, channel, installed, expected, root, app, updateExe, packages) =
    (args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]);
Directory.CreateDirectory(packages);

var locator = new TestVelopackLocator("HashTool", installed, packages, app, root, updateExe, channel);
var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(releases)), new UpdateOptions { ExplicitChannel = channel }, locator);

var update = manager.CheckForUpdates();
if (update is null)
{
    Console.Error.WriteLine($"Velopack 0.0.1298 found no update for {installed} on channel '{channel}' in {releases}.");
    return 1;
}

var found = update.TargetFullRelease.Version.ToString();
if (!string.Equals(found, expected, StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Velopack 0.0.1298 offered {found}, expected {expected}.");
    return 1;
}

manager.DownloadUpdates(update);
var package = Path.Combine(packages, update.TargetFullRelease.FileName);
if (!File.Exists(package))
{
    Console.Error.WriteLine($"The downloaded package is missing: {package}");
    return 1;
}

Console.Error.WriteLine($"Velopack 0.0.1298 downloaded and verified {update.TargetFullRelease.FileName} ({found}).");
Console.WriteLine(package);
return 0;
