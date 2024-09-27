// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#addin nuget:?package=Cake.FileHelpers&version=5.0.0
#addin nuget:?package=Cake.Git&version=2.0.0
#addin nuget:?package=Cake.Incubator&version=7.0.0
#addin nuget:?package=Cake.SemVer&version=4.0.0
#addin nuget:?package=semver&version=2.0.4
#addin nuget:?package=Cake.Json&version=7.0.1
#load "scripts/utility.cake"
#load "scripts/configuration/config-parser.cake"

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

const string AndroidSdkRepoName = "microsoft/appcenter-sdk-android";
const string AppleSdkRepoName = "microsoft/appcenter-sdk-apple";
const string GitHubTagsUrlFormat = "repos/{0}/tags";

// Task TARGET for build
var TARGET = Argument("target", Argument("t", ""));

// Need to read versions before setting url values
VersionReader.ReadVersions();

Task("IncrementRevisionNumberWithHash").Does(() =>
{
    IncrementRevisionNumber(true);
});

Task("IncrementRevisionNumber").Does(() =>
{
    IncrementRevisionNumber(false);
});

Task("SetReleaseVersion").Does(() =>
{
    var prereleaseSuffix = Argument<string>("PrereleaseSuffix", null);

    // Get base version of .NET standard core
    var releaseVersion = GetBaseSemanticVersion();

    Warning($"Release version: {releaseVersion}");

    // Append suffix if any is provided for this release
    if (prereleaseSuffix != null) {
        releaseVersion = $"{releaseVersion}-{prereleaseSuffix}";
    }

    // Replace versions in all non-demo app files
    var informationalVersionPattern = @"AssemblyInformationalVersion\(" + "\".*\"" + @"\)";
    ReplaceRegexInFilesWithExclusion("**/AssemblyInfo.cs", informationalVersionPattern, "AssemblyInformationalVersion(\"" + releaseVersion + "\")", "Demo");
    UpdateNewProjSdkVersion(releaseVersion, releaseVersion);
    UpdateWrapperSdkVersion(releaseVersion);
    UpdateConfigFileSdkVersion(releaseVersion);
});

Task("UpdateDemoVersion").Does(()=>
{
    var newVersion = Argument<string>("DemoVersion");

    // Replace version in all the demo application assemblies
    var demoAssemblyInfoGlob = "Apps/**/*Demo*/**/AssemblyInfo.cs";
    var informationalVersionPattern = @"AssemblyInformationalVersion\(" + "\".*\"" + @"\)";
    ReplaceRegexInFiles(demoAssemblyInfoGlob, informationalVersionPattern, "AssemblyInformationalVersion(\"" + newVersion + "\")");
    var newFileVersion = GetBaseVersion(newVersion) + "." + GetRevisionNumber(newVersion);
    var fileVersionPattern = @"AssemblyFileVersion\(" + "\".*\"" + @"\)";
    ReplaceRegexInFiles(demoAssemblyInfoGlob, fileVersionPattern, "AssemblyFileVersion(\"" + newFileVersion + "\")");
    var csprojFiles = GetFiles("Apps/**/*Demo*/**/*.csproj");
    foreach (var file in csprojFiles)
    {
        UpdateNewProjVersion(file, newVersion, newFileVersion);
    }

    // Replace android versions
    var manifestGlob = "Apps/**/*Demo*/**/AndroidManifest.xml";

    // Manifest version name tag
    var versionNamePattern = "android:versionName=\"[^\"]+\"";
    var newVersionName = "android:versionName=\"" + newVersion + "\"";
    ReplaceRegexInFilesWithExclusion(manifestGlob, versionNamePattern, newVersionName, "/bin/", "/obj/");

    // Manifest version code
    var manifests = GetFiles("Apps/**/*Demo*/**/AndroidManifest.xml");
    foreach (var manifest in manifests)
    {
        if (!manifest.FullPath.Contains("/bin/") &&
            !manifest.FullPath.Contains("/obj/"))
        {
            IncrementManifestVersionCode(manifest);
        }
    }

    // Replace UWP version
    var uwpManifestGlob = "Apps/**/*Demo*/**/Package.appxmanifest";
    var versionTagPattern = " Version=\"[^\"]+\"";
    var newVersionTagText = " Version=\"" + newFileVersion + "\"";
    ReplaceRegexInFiles(uwpManifestGlob, versionTagPattern, newVersionTagText);

    // Replace iOS version
    var bundleVersionPattern = @"<key>CFBundleVersion<\/key>\s*<string>[^<]*<\/string>";
    var newBundleVersionString = "<key>CFBundleVersion</key>\n\t<string>" + newVersion + "</string>";
    ReplaceRegexInFilesWithExclusion("Apps/**/*Demo*/**/Info.plist", bundleVersionPattern, newBundleVersionString, "/bin/", "/obj/");
    var bundleShortVersionPattern = @"<key>CFBundleShortVersionString<\/key>\s*<string>[^<]*<\/string>";
    var newBundleShortVersionString = "<key>CFBundleShortVersionString</key>\n\t<string>" + newVersion + "</string>";
    ReplaceRegexInFilesWithExclusion("Apps/**/*Demo*/**/Info.plist", bundleShortVersionPattern, newBundleShortVersionString, "/bin/", "/obj/");

    // Note: nuget update does not work with projects using project.json
    // Replace version in all the demo application
    ReplaceRegexInFiles("Apps/**/*Demo*/**/project.json", "(Microsoft.AppCenter[^\"]*\":[ ]+\")[^\"]+", "$1" + newVersion, RegexOptions.ECMAScript);
    ReplaceRegexInFiles("Apps/**/*Demo*/**/*.csproj",
            "<PackageReference Include=\"(Microsoft.AppCenter[^\"]*)\" Version=\"[^\"]+\" />",
            "<PackageReference Include=\"$1\" Version=\"" + newVersion + "\" />", RegexOptions.ECMAScript);
});

Task("StartNewVersion").Does(() =>
{
    var newVersion = Argument<string>("NewVersion");
    StartNewVersion(newVersion);
});

// Fills Android and iOS versions in the build config file with the relevant ones.
Task("UpdateNativeVersionsToLatest")
    .IsDependentOn("UpdateAndroidVersionToLatest")
    .IsDependentOn("UpdateAppleVersionToLatest");

Task("UpdateAndroidVersionToLatest").Does(() => 
{
    var androidLatestVersion = GetLatestGitHubReleaseVersion(AndroidSdkRepoName);
    Information($"Received latest android sdk release version {androidLatestVersion}. Verifying if it's a valid semver version...");
    ParseSemVer(androidLatestVersion);
    var versionsAreEqual = VersionReader.AndroidVersion.Equals(androidLatestVersion);
    if (versionsAreEqual) 
    {
        Information($"Nothing to replace. Exiting...");
        return;
    }
    VersionReader.WriteAndroidVersion(androidLatestVersion);
}).OnError(HandleError);

Task("UpdateAppleVersionToLatest").Does(() => 
{
    var appleLatestVersion = GetLatestGitHubReleaseVersion(AppleSdkRepoName);
    Information($"Received latest apple sdk release version {appleLatestVersion}. Verifying if it's a valid semver version...");
    ParseSemVer(appleLatestVersion);
    var versionsAreEqual = VersionReader.AppleVersion.Equals(appleLatestVersion);
    if (versionsAreEqual) 
    {
        Information($"Nothing to replace. Exiting...");
        return;
    }
    VersionReader.WriteAppleVersion(appleLatestVersion);
}).OnError(HandleError);

Task("IncreasePatchVersion").Does(() => 
{
    var sdkVersion = ParseSemVer(VersionReader.SdkVersion);
    var patchVersion = sdkVersion.Patch;
    patchVersion++;
    sdkVersion = sdkVersion.Change(patch: patchVersion, prerelease: "", build: "");
    Information($"Bumping {VersionReader.SdkVersion} SDK version to {sdkVersion.ToString()}...");
    StartNewVersion(sdkVersion.ToString());
}).OnError(HandleError);

void StartNewVersion(string newVersion)
{
    var snapshotVersion = newVersion + "-SNAPSHOT";
    var newFileVersion = newVersion + ".0";

    // Replace version in all but the demo application assemblies
    var assemblyInfoGlob = "**/AssemblyInfo.cs";
    var informationalVersionPattern = @"AssemblyInformationalVersion\(" + "\".*\"" + @"\)";
    ReplaceRegexInFilesWithExclusion(assemblyInfoGlob, informationalVersionPattern, "AssemblyInformationalVersion(\"" + snapshotVersion + "\")", "Demo");
    var fileVersionPattern = @"AssemblyFileVersion\(" + "\".*\"" + @"\)";
    ReplaceRegexInFilesWithExclusion(assemblyInfoGlob, fileVersionPattern, "AssemblyFileVersion(\"" + newFileVersion + "\")", "Demo");

    UpdateConfigFileSdkVersion(snapshotVersion);
    UpdateNewProjSdkVersion(snapshotVersion, newFileVersion);
    UpdateWrapperSdkVersion(snapshotVersion);

    // Replace android manifest version name tag
    var androidManifestGlob = "**/AndroidManifest.xml";
    var versionNamePattern = "android:versionName=\"[^\"]+\"";
    var newVersionName = "android:versionName=\"" + snapshotVersion + "\"";
    ReplaceRegexInFilesWithExclusion(androidManifestGlob, versionNamePattern, newVersionName, "Demo", "/bin/", "/obj/");

    // Replace android manifest version code
    var manifests = GetFiles(androidManifestGlob);
    foreach (var manifest in manifests)
    {
        if (!manifest.FullPath.Contains("Demo") &&
            !manifest.FullPath.Contains("SDK") &&
            !manifest.FullPath.Contains("externals") &&
            !manifest.FullPath.Contains("/bin/") &&
            !manifest.FullPath.Contains("/obj/"))
        {
            IncrementManifestVersionCode(manifest);
        }
    }

    // Replace UWP version
    var uwpManifestGlob = "**/Package.appxmanifest";
    var versionTagPattern = " Version=\"[^\"]+\"";
    var newVersionTagText = " Version=\""+newVersion+".0\"";
    ReplaceRegexInFilesWithExclusion(uwpManifestGlob, versionTagPattern, newVersionTagText, "Demo");

    // Replace iOS version
    var bundleVersionPattern = @"<key>CFBundleVersion<\/key>\s*<string>[^<]*<\/string>";
    var newBundleVersionString = "<key>CFBundleVersion</key>\n\t<string>" + newVersion + "</string>";
    ReplaceRegexInFilesWithExclusion("**/Info.plist", bundleVersionPattern, newBundleVersionString, "/bin/", "/obj/", "Demo");
    var bundleShortVersionPattern = @"<key>CFBundleShortVersionString<\/key>\s*<string>[^<]*<\/string>";
    var newBundleShortVersionString = "<key>CFBundleShortVersionString</key>\n\t<string>" + newVersion + "</string>";
    ReplaceRegexInFilesWithExclusion("**/Info.plist", bundleShortVersionPattern, newBundleShortVersionString, "/bin/", "/obj/", "Demo");
}

void IncrementRevisionNumber(bool useHash)
{
    // Get base version of .NET standard core
    var baseSemanticVersion = GetBaseSemanticVersion();
    var nugetVer = GetLatestNuGetVersion();
    var baseVersion = GetBaseVersion(nugetVer);
    var newRevNum = baseSemanticVersion == baseVersion ? GetRevisionNumber(nugetVer) + 1 : 1;
    var newRevString = GetPaddedString(newRevNum, 4);
    var newVersion = baseSemanticVersion + "-r" + newRevString;
    var newFileVersion = baseSemanticVersion + "." + newRevNum;
    if (useHash)
    {
        newVersion += "-" + GetShortCommitHash();
    }
    Information("Set version to " + newVersion);

    // Replace AssemblyInformationalVersion in all AssemblyInfo files
    var informationalVersionPattern = @"AssemblyInformationalVersion\(" + "\".*\"" + @"\)";
    ReplaceRegexInFiles("**/AssemblyInfo.cs", informationalVersionPattern, "AssemblyInformationalVersion(\"" + newVersion + "\")");

    // Increment revision number of AssemblyFileVersion
    var fileVersionPattern = @"AssemblyFileVersion\(" + "\".*\"" + @"\)";
    var files = FindRegexInFiles("**/AssemblyInfo.cs", fileVersionPattern);
    foreach (var file in files)
    {
        var fileVersionTrimmedPattern = @"AssemblyFileVersion\("+ "\"" + @"([0-9]+.){3}";
        var fullVersion = FindRegexMatchInFile(file, fileVersionPattern, RegexOptions.None);
        var trimmedVersion = FindRegexMatchInFile(file, fileVersionTrimmedPattern, RegexOptions.None);
        var newFileVersionTmp = trimmedVersion + newRevNum + "\")";
        ReplaceTextInFiles(file.FullPath, fullVersion, newFileVersionTmp);
    }

    UpdateConfigFileSdkVersion(newVersion);
    UpdateNewProjSdkVersion(newVersion, newFileVersion);
    UpdateWrapperSdkVersion(newVersion);
}

string GetBaseSemanticVersion()
{
    return GetBaseVersion(VersionReader.SdkVersion);
}

string GetShortCommitHash()
{
    var lastCommit = GitLogTip(".");
    return lastCommit.Sha.Substring(0, 7);
}

string GetLatestNuGetVersion()
{
    //Since password and feed id are secret variables in VSTS (and thus cannot be accessed like other environment variables),
    //provide the option to pass them as parameters to the cake script
    var nugetUser = EnvironmentVariable("NUGET_USER");
    var nugetPassword = Argument("NuGetPassword", EnvironmentVariable("NUGET_PASSWORD"));
    var nugetFeedId = Argument("NuGetFeedId", EnvironmentVariable("NUGET_FEED_ID"));
    var url = "https://msmobilecenter.pkgs.visualstudio.com/_packaging/" + nugetFeedId + "/nuget/v2/Search()?$filter=IsAbsoluteLatestVersion+and+Id+eq+'Microsoft.AppCenter'&includePrerelease=true";
    HttpWebRequest request = (HttpWebRequest)WebRequest.Create (url);
    request.Headers["X-NuGet-ApiKey"] = nugetPassword;
    request.Credentials = new NetworkCredential(nugetUser, nugetPassword);
    HttpWebResponse response = (HttpWebResponse)request.GetResponse ();
    var responseString = String.Empty;
    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
    {
        responseString = reader.ReadToEnd();
    }
    var startTag = "<d:Version>";
    var endTag = "</d:Version>";
    int start = responseString.IndexOf(startTag);
    int end = responseString.IndexOf(endTag);
    if (start == -1 || end == -1) {
        return "0.0.0";
    }
    var tag = responseString.Substring(start + startTag.Length, end - start - startTag.Length);
    return tag;
}

void IncrementManifestVersionCode(FilePath manifest)
{
    var versionCodePattern = "android:versionCode=\"[^\"]+\"";
    var versionCodeText = FindRegexMatchInFile(manifest, versionCodePattern, RegexOptions.None);
    if (string.IsNullOrEmpty(versionCodeText))
    {
        return;
    }
    var firstPart = "android:versionCode=\"";
    var length = versionCodeText.Length - 1 - firstPart.Length;
    var versionCode = int.Parse(versionCodeText.Substring(firstPart.Length, length));
    var newVersionCodeText = firstPart + (versionCode + 1) + "\"";
    ReplaceRegexInFiles(manifest.FullPath, versionCodePattern, newVersionCodeText);
}

string GetBaseVersion(string fullVersion)
{
    var indexDash = fullVersion.IndexOf("-");
    if (indexDash == -1)
    {
        return fullVersion;
    }
    return fullVersion.Substring(0, indexDash);
}

// Changes the Version field in WrapperSdk.cs to the given version
void UpdateWrapperSdkVersion(string newVersion)
{
    var patternString = "Version = \"[^\"]+\";";
    var newString = "Version = \"" + newVersion + "\";";
    ReplaceRegexInFiles("SDK/AppCenter/Microsoft.AppCenter.Shared/WrapperSdk.cs", patternString, newString);
}

void UpdateNewProjSdkVersion(string newVersion, string newFileVersion)
{
    var csprojFiles = GetFiles("**/*.csproj");
    foreach (var file in csprojFiles)
    {
        if (!file.FullPath.Contains("Demo"))
        {
            UpdateNewProjVersion(file, newVersion, newFileVersion);
        }
    }
    var propsFiles = GetFiles("**/Directory.Build.props");
    foreach (var file in propsFiles)
    {
            UpdateNewProjVersion(file, newVersion, newFileVersion);
    }
}

void UpdateNewProjVersion(FilePath file, string newVersion, string newFileVersion)
{
    var csproj = XDocument.Load(file.FullPath);
    var version = csproj.XPathSelectElement("/Project/PropertyGroup/Version");
    version?.SetValue(newVersion);
    var fileVersion = csproj.XPathSelectElement("/Project/PropertyGroup/FileVersion");
    fileVersion?.SetValue(newFileVersion);
    if (version != null || fileVersion != null)
    {
        csproj.Save(file.FullPath);
    }
}

// Gets the revision number from a version string containing revision -r****
int GetRevisionNumber(string fullVersion)
{
    var revStart = fullVersion.IndexOf("-r");
    if (revStart == -1)
    {
        return 0;
    }
    var revEnd = fullVersion.IndexOf("-", revStart + 1);
    if (revEnd == -1)
    {
        revEnd = fullVersion.Length;
    }
    var revString = fullVersion.Substring(revStart + 2, revEnd - revStart - 2);
    try
    {
        return Int32.Parse(revString);
    }
    catch
    {
        return 0; //if the revision number could not be parsed, start new revision
    }
}

// Returns the given integer as a string with a number of leading zeroes to
// pad the string to numDigits digits
string GetPaddedString(int num, int numDigits)
{
    var numString = num.ToString();
    while (numString.Length < numDigits)
    {
        numString = "0" + numString;
    }
    return numString;
}

// Run "ReplaceRegexInFiles" methods but exclude all file paths containing the strings in "excludeFilePathsContaining"
void ReplaceRegexInFilesWithExclusion(string globberPattern, string regEx, string replacement, params string[] excludeFilePathsContaining)
{
    var files = GetFiles(globberPattern);
    foreach (var file in files)
    {
        bool shouldReplace = true;
        foreach (var excludeString in excludeFilePathsContaining)
        {
            if (file.FullPath.Contains(excludeString))
            {
                shouldReplace = false;
                break;
            }
        }
        if (shouldReplace)
        {
            ReplaceRegexInFiles(file.FullPath, regEx, replacement);
        }
    }
}

public void UpdateConfigFileSdkVersion(string newVersion)
{
    ReplaceRegexInFiles(ConfigFile.Path, @"<sdkVersion>[\.|A-z|0-9|-]*<\/sdkVersion>", $"<sdkVersion>{newVersion}</sdkVersion>");
}

Task("UpdateCgManifest").Does(()=>
{
    try
    {
        var manifestFilePath = "cgmanifest.json";
        var content = ParseJsonFromFile(manifestFilePath);
        var registrations = (JArray)content["Registrations"];
        foreach (var registration in registrations.Children())
        {
            HanldeRegistration(registration);
        }

        SerializeJsonToPrettyFile(manifestFilePath, content);
    }
    catch (Exception e)
    {
        Warning($"Can't update 'cgmanifest.json'. Error message: {e.Message}");
    }
});

void HanldeRegistration(JToken registration)
{
    var component = registration["component"];
    if (component == null) 
    {
        Warning("Current registration has no 'component' property.");
        return;
    }

    var typeObject = component["type"];
    if (typeObject == null || typeObject.Value<string>() != "git")
    {
        Warning("Current component has no field 'type' or 'type' is not 'git'.");
        return;
    }

    UpdateCommitHash(component);
}

void UpdateCommitHash(JToken component)
{
    var gitData = component["git"];
    var repoUrl = gitData["repositoryUrl"].Value<string>();
    var currentRepoName = repoUrl.Contains(AndroidSdkRepoName) ? AndroidSdkRepoName : AppleSdkRepoName;
    var releaseTag = GetReleaseTag(currentRepoName);
    if (string.IsNullOrEmpty(releaseTag))
    {
        Warning($"Repository url: {repoUrl}. Release tag '{releaseTag}' was not found.");
        return;
    }

    var tagsRequest = CreateGitHubRequest(string.Format(GitHubTagsUrlFormat, currentRepoName));
    var tagsListJson = GetResponseJsonArray(tagsRequest);
    var tag = tagsListJson.Children().FirstOrDefault(t => t["name"].Value<string>() == releaseTag);
    if (tag == null)
    {
        Warning($"Repository url: {repoUrl}. Tag '{tag}' was not found.");
        return;
    }

    gitData["commitHash"] = tag["commit"]["sha"].Value<string>();
}

string GetReleaseTag(string currentRepoName)
{
    switch (currentRepoName)
    {
        case AndroidSdkRepoName:
            return VersionReader.AndroidVersion;
        case AppleSdkRepoName:
            return VersionReader.AppleVersion;
        default:
            return null;
    }
}

RunTarget(TARGET);
