﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AppCenter.Test.Functional.Distribute
{
    using Distribute = Microsoft.AppCenter.Distribute.Distribute;
    using AppCenter = Microsoft.AppCenter.AppCenter;
    using UpdateTrack = Microsoft.AppCenter.Distribute.UpdateTrack;

    public enum DistributeTestType
    {
        SaveMockUpdateToken,
        EnableDebuggableBuilds,
        FreshInstallAsync,
        CheckUpdateAsync,
        OnResumeActivity,
        Clear
    }

    public class DistributeUpdateTest
    {
        public delegate void DistributeEventHandler(object sender, DistributeTestType e);

        public static event DistributeEventHandler DistributeEvent;

        // Before
        public DistributeUpdateTest()
        {
            Utils.DeleteDatabase();
            Distribute.UnsetInstance();
            DistributeEvent?.Invoke(this, DistributeTestType.Clear);
        }

        [Fact]
        public async Task GetLastReleaseDetailsAsync()
        {
            // Enable Distribute for debuggable builds.
            DistributeEvent?.Invoke(this, DistributeTestType.EnableDebuggableBuilds);

            // Save data to preference.
            DistributeEvent?.Invoke(this, DistributeTestType.CheckUpdateAsync);

            // Setup network adapter.
            var httpNetworkAdapter = new HttpNetworkAdapter();
            DependencyConfiguration.HttpNetworkAdapter = httpNetworkAdapter;
            var eventTask = httpNetworkAdapter.MockRequest(request => request.Method == "GET");
            var startServiceTask = httpNetworkAdapter.MockRequestByLogType("startService");

            // Start AppCenter.
            AppCenter.UnsetInstance();
            AppCenter.LogLevel = LogLevel.Verbose;
            AppCenter.Start(Config.ResolveAppSecret(), typeof(Distribute));

            // Wait for "startService" log to be sent.
            await startServiceTask;
            DistributeEvent?.Invoke(this, DistributeTestType.OnResumeActivity);

            // Wait when Distribute will start.
            await Distribute.IsEnabledAsync();

            // Wait for processing event.
            var result = await eventTask;
        
            // Verify response.
            Assert.Equal("GET", result.Method);
            Assert.Contains("releases/latest?", result.Uri);
            Assert.Contains("release_hash=", result.Uri);
            Assert.Contains(Config.ResolveAppSecret(), result.Uri);

            // Clear.
            DistributeEvent?.Invoke(this, DistributeTestType.Clear);
        }

        [Fact]
        public async Task SetUpdateTrackPublicTest()
        {
            // Enable Distribute for debuggable builds.
            DistributeEvent?.Invoke(this, DistributeTestType.EnableDebuggableBuilds);

            // Setup network adapter.
            var httpNetworkAdapter = new HttpNetworkAdapter();
            DependencyConfiguration.HttpNetworkAdapter = httpNetworkAdapter;
            var eventTask = httpNetworkAdapter.MockRequest(request => request.Method == "GET");
            var startServiceTask = httpNetworkAdapter.MockRequestByLogType("startService");

            // Start AppCenter.
            AppCenter.UnsetInstance();
            AppCenter.LogLevel = LogLevel.Verbose;
            Distribute.UpdateTrack = UpdateTrack.Public;
            AppCenter.Start(Config.ResolveAppSecret(), typeof(Distribute));

            // Wait for "startService" log to be sent.
            await startServiceTask;
            DistributeEvent?.Invoke(this, DistributeTestType.OnResumeActivity);

            // Wait when Distribute will start.
            await Distribute.IsEnabledAsync();

            // Wait for processing event.
            var result = await eventTask;

            // Verify response.
            Assert.Equal("GET", result.Method);
            Assert.Contains("public", result.Uri);
            Assert.Contains("releases/latest", result.Uri);
            Assert.Contains(Config.ResolveAppSecret(), result.Uri);

            // Clear.
            DistributeEvent?.Invoke(this, DistributeTestType.Clear);
        }

        [Fact]
        public async Task SetUpdateTrackPrivateTest()
        {
            // Enable Distribute for debuggable builds.
            DistributeEvent?.Invoke(this, DistributeTestType.EnableDebuggableBuilds);

            // Save data to preference.
            DistributeEvent?.Invoke(this, DistributeTestType.CheckUpdateAsync);

            // Setup network adapter.
            var httpNetworkAdapter = new HttpNetworkAdapter();
            DependencyConfiguration.HttpNetworkAdapter = httpNetworkAdapter;
            var eventTask = httpNetworkAdapter.MockRequest(request => request.Method == "GET");
            var startServiceTask = httpNetworkAdapter.MockRequestByLogType("startService");

            // Start AppCenter.
            AppCenter.UnsetInstance();
            AppCenter.LogLevel = LogLevel.Verbose;
            Distribute.UpdateTrack = UpdateTrack.Private;

            // MockUpdateToken.
            DistributeEvent?.Invoke(this, DistributeTestType.SaveMockUpdateToken);
            AppCenter.Start(Config.ResolveAppSecret(), typeof(Distribute));

            // Wait for "startService" log to be sent.
            await startServiceTask;
            DistributeEvent?.Invoke(this, DistributeTestType.OnResumeActivity);

            // Wait when Distribute will start.
            await Distribute.IsEnabledAsync();

            // Wait for processing event.
            var result = await eventTask;

            // Verify response.
            Assert.Equal("GET", result.Method);
            Assert.DoesNotContain("public", result.Uri);
            Assert.Contains("releases/private/latest?", result.Uri);
            Assert.Contains("release_hash=", result.Uri);
            Assert.Contains(Config.ResolveAppSecret(), result.Uri);

            // Clear.
            DistributeEvent?.Invoke(this, DistributeTestType.Clear);
        }

        [Theory]
        [InlineData(new object[] { null, "releases/latest" })]
        [InlineData(new object[] { UpdateTrack.Private, "releases/private/latest" })]
        [InlineData(new object[] { UpdateTrack.Public, "releases/latest" })]
        public async Task CheckForUpdateTest(UpdateTrack updateTrack, string urlDiff)
        {
            // Enable Distribute for debuggable builds.
            DistributeEvent?.Invoke(this, DistributeTestType.EnableDebuggableBuilds);

            // Setup network adapter.
            var httpNetworkAdapter = new HttpNetworkAdapter();
            DependencyConfiguration.HttpNetworkAdapter = httpNetworkAdapter;
            var implicitCheckForUpdateTask = httpNetworkAdapter.MockRequest(request => request.Method == "GET" && request.Uri.Contains(urlDiff));
            var startServiceTask = httpNetworkAdapter.MockRequestByLogType("startService");

            // Start AppCenter.
            AppCenter.UnsetInstance();
            AppCenter.LogLevel = LogLevel.Verbose;
            Distribute.UpdateTrack = updateTrack;

            // Save update token.
            if (updateTrack == UpdateTrack.Private)
            {
                DistributeEvent?.Invoke(this, DistributeTestType.SaveMockUpdateToken);
            }
            AppCenter.Start(Config.ResolveAppSecret(), typeof(Distribute));

            // Wait for "startService" log to be sent.
            await startServiceTask;
            DistributeEvent?.Invoke(this, DistributeTestType.OnResumeActivity);

            // Wait when Distribute will start.
            await Distribute.IsEnabledAsync();

            // Wait for processing event.
            var resultImplicit = await implicitCheckForUpdateTask;

            // Verify response.
            Assert.Equal("GET", resultImplicit.Method);
            Assert.Contains(urlDiff, resultImplicit.Uri);
            Assert.Contains(Config.ResolveAppSecret(), resultImplicit.Uri);

            // Wait a 5s for give time to complete internal processes
            // to avoid this case `A check for update is already ongoing.`
            await Task.Delay(5000);

            // Check for update.
            var explicitCheckForUpdateTask = httpNetworkAdapter.MockRequest(request => request.Method == "GET" && request.Uri.Contains(urlDiff));
            Distribute.CheckForUpdate();

            // Wait for processing event.
            var resultExplicit = await explicitCheckForUpdateTask;

            // Verify response.
            Assert.Equal("GET", resultExplicit.Method);
            Assert.Contains(urlDiff, resultExplicit.Uri);
            Assert.Contains(Config.ResolveAppSecret(), resultExplicit.Uri);
        }

        [Fact]
        public async Task DisableAuthomaticCheckUpdateTest()
        {
            // Enable Distribute for debuggable builds.
            DistributeEvent?.Invoke(this, DistributeTestType.EnableDebuggableBuilds);

            // Setup network adapter.
            var httpNetworkAdapter = new HttpNetworkAdapter();
            DependencyConfiguration.HttpNetworkAdapter = httpNetworkAdapter;
            var explicitCheckForUpdateTask = httpNetworkAdapter.MockRequest(request => request.Method == "GET");
            var startServiceTask = httpNetworkAdapter.MockRequestByLogType("startService");

            // Start AppCenter.
            AppCenter.UnsetInstance();
            AppCenter.LogLevel = LogLevel.Verbose;
            Distribute.DisableAutomaticCheckForUpdate();
            AppCenter.Start(Config.ResolveAppSecret(), typeof(Distribute));

            // Wait for "startService" log to be sent.
            await startServiceTask;
            Assert.Equal(1, httpNetworkAdapter.CallCount);
            DistributeEvent?.Invoke(this, DistributeTestType.OnResumeActivity);

            // Wait when Distribute will start.
            await Distribute.IsEnabledAsync();

            // Wait a 5s and verify that we will not have new calls.
            await Task.Delay(5000);
            Assert.Equal(1, httpNetworkAdapter.CallCount);

            // Check for update.
            Distribute.CheckForUpdate();

            // Wait for processing event.
            var result = await explicitCheckForUpdateTask;

            // Verify response.
            Assert.Equal(2, httpNetworkAdapter.CallCount);
            Assert.Equal("GET", result.Method);
            Assert.Contains("releases/latest", result.Uri);
            Assert.Contains(Config.ResolveAppSecret(), result.Uri);
        }
    }
}
