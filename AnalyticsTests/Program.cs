using SOCYVIA.Models;
using SOCYVIA.Services;
using System.Net;
using System.Text.Json;

var isolatedStorage = Path.Combine(Path.GetTempPath(), "socyvia-final-gates-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("SOCYVIA_STORAGE_ROOT", isolatedStorage);

var dataset = new AnalysisDataset
{
    StudyId = "analytics-study", DatasetHash = "fixture-hash",
    Variables = [new AnalysisVariable { Id = "qualified_impressions", Name = "Qualified impressions", Source = "REMOTE_BEHAVIORAL_TELEMETRY", DataType = "Double", MeasurementLevel = MeasurementLevels.Count }],
    Rows =
    [
        new AnalysisRow { ParticipantId = "a", SessionId = "s-a", GroupName = "Group A", ConditionName = "A", SessionCompleted = true, NumericValues = new() { ["qualified_impressions"] = 2 } },
        new AnalysisRow { ParticipantId = "b", SessionId = "s-b", GroupName = "Group B", ConditionName = "B", SessionCompleted = true, NumericValues = new() { ["qualified_impressions"] = 6 } }
    ]
};
var result = ScientificAnalysisEngine.Execute(dataset, new AnalysisSpecification { StudyId = dataset.StudyId, OutcomeVariableId = "qualified_impressions", Method = "DESCRIPTIVE" });
if (result.Result?.N != 2 || result.Result.ResultData["Mean"] != 4) throw new Exception("Deterministic descriptive analysis did not preserve the eligible sample.");
var figure = ResearchFigureService.CreateGroupedMeanFigure(dataset, "qualified_impressions", "group");
if (!figure.Svg.Contains("Group A", StringComparison.Ordinal) || !figure.Svg.Contains("Group B", StringComparison.Ordinal) || !figure.CsvData.Contains("2", StringComparison.Ordinal) || !figure.CsvData.Contains("6", StringComparison.Ordinal)) throw new Exception("Figure output did not preserve group-separated source values.");
var quality = DataQualityService.Evaluate(dataset);
var request = ResearchInterpretationService.BuildRequest(new Study { Id = dataset.StudyId, Title = "Fixture" }, dataset, quality, [result]);
var ai = await ResearchInterpretationService.InterpretAsync(request);
if (ai.Status != ResearchInterpretationResponse.NotConfigured || ai.Interpretation is not null || string.IsNullOrWhiteSpace(ai.InputHash)) throw new Exception("Unconfigured AI must not fabricate an interpretation.");
var source = SourceSnapshotService.Freeze(new AcquiredContentMetadata { OriginalUrl = "https://example.org/article", SourceName = "Example", BodyText = "Reviewed source text." }, new SourceEngagementCounts(Likes: 18472, Comments: 10328), [new SourceStimulusComment("c1", "A", "First", Reactions: 2), new SourceStimulusComment("c2", "B", "Second", Reactions: 9)]);
var displayed = SourceSnapshotService.SelectDisplayedComments(source, new SourceCommentPresentation(SourceCommentSelectionStrategy.Top, 1));
if (source.Engagement.Comments != 10328 || displayed.Single().Id != "c2" || string.IsNullOrWhiteSpace(source.SnapshotHash)) throw new Exception("Source snapshot counts, immutable identity, or comment selection were not preserved.");
var updateCanonical = System.Text.Json.JsonSerializer.Serialize(new { Channel = "stable", Version = "1.0.1", PublishedAtUtc = DateTime.UnixEpoch, Artifacts = new Dictionary<string, ReleaseArtifact> { ["win-x64"] = new("win-x64", new Uri("https://updates.example.org/socyvia.exe"), new string('a', 64), 100) }, KeyId = "fixture" });
var updateManifest = new ReleaseManifest("stable", "1.0.1", DateTime.UnixEpoch, new Dictionary<string, ReleaseArtifact> { ["win-x64"] = new("win-x64", new Uri("https://updates.example.org/socyvia.exe"), new string('a', 64), 100) }, "fixture", "signature", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(updateCanonical))).ToLowerInvariant());
var update = new ReleaseUpdateService(new FixtureManifestVerifier()).Evaluate("1.0.0", System.Text.Json.JsonSerializer.Serialize(updateManifest), "win-x64");
if (update.State != ReleaseUpdateState.UpdateAvailable || update.Artifact?.Platform != "win-x64") throw new Exception("Update readiness must require a verified platform-specific manifest.");
var arabic = "المشاركون والجلسات ونتائج البحث";
if (await ProductTextPersistenceService.RoundTripProductCopyAsync(arabic) != arabic) throw new Exception("Arabic product copy must survive the SQLite persistence/display boundary exactly.");
if (SocyviaProductUrls.ParticipantDemoUri.AbsoluteUri != "https://socyvia.com/experimentfeed/demo") throw new Exception("The public SOCYVIA Demo URL must remain stable.");
Uri? launched = null; SocyviaProductUrls.OpenParticipantDemo(uri => launched = uri);
if (launched != SocyviaProductUrls.ParticipantDemoUri || launched!.Host is "localhost" or "127.0.0.1") throw new Exception("The public Demo must never target a local host.");
Uri? mediaSetupLaunched = null; SocyviaProductUrls.OpenCloudflareMediaStorageSetup(uri => mediaSetupLaunched = uri);
if (mediaSetupLaunched != SocyviaProductUrls.CloudflareMediaStorageSetupUri ||
    mediaSetupLaunched!.Scheme != Uri.UriSchemeHttps || mediaSetupLaunched.Host != "dash.cloudflare.com")
    throw new Exception("Media storage setup must use the official Cloudflare HTTPS activation surface.");
var oauthClient = new CloudflareOAuthClientConfiguration("public-client", new Uri("https://socyvia.com/oauth/cloudflare/callback"), ["account:read"]);
var releaseOAuth = CloudflareOAuthClientConfiguration.LoadReleaseConfiguration();
if (releaseOAuth.ClientId != CloudflareOAuthClientConfiguration.OfficialClientId ||
    releaseOAuth.RedirectUri != CloudflareOAuthClientConfiguration.OfficialRedirectUri ||
    !releaseOAuth.Scopes.SequenceEqual(["d1.read", "d1.write", "workers-scripts.read", "workers-scripts.write"]))
    throw new Exception("The release OAuth client ID, redirect URI, or verified least-privilege scopes changed.");
if (!CloudflareDesktopOAuth.TryCreateAuthorizationRequest(oauthClient, out var oauthRequest) || oauthRequest is null || oauthRequest.AuthorizationUri.Scheme != "https" || !oauthRequest.AuthorizationUri.Query.Contains("code_challenge_method=S256", StringComparison.Ordinal) || oauthRequest.AuthorizationUri.Query.Contains("client_secret", StringComparison.Ordinal)) throw new Exception("Cloudflare desktop authorization must use a secret-free HTTPS PKCE request.");
if (CloudflareDesktopOAuth.TryCreateAuthorizationRequest(oauthClient with { ClientId = string.Empty }, out _)) throw new Exception("Cloudflare authorization must not begin before public-client registration is configured.");
var validCallback = new Uri($"socyvia://oauth/cloudflare/callback?state={Uri.EscapeDataString(oauthRequest.State)}&code=one-time-code");
if (!CloudflareDesktopOAuth.ValidateCallback(validCallback, oauthRequest).IsValid ||
    CloudflareDesktopOAuth.ValidateCallback(new Uri($"other://oauth/cloudflare/callback?state={oauthRequest.State}&code=x"), oauthRequest).IsValid ||
    CloudflareDesktopOAuth.ValidateCallback(new Uri("socyvia://oauth/cloudflare/callback?state=wrong&code=x"), oauthRequest).IsValid ||
    CloudflareDesktopOAuth.ValidateCallback(new Uri($"socyvia://oauth/cloudflare/callback?state={oauthRequest.State}"), oauthRequest).IsValid ||
    CloudflareDesktopOAuth.ValidateCallback(new Uri($"socyvia://oauth/cloudflare/callback?state={oauthRequest.State}&error=access_denied"), oauthRequest).IsValid ||
    CloudflareDesktopOAuth.ValidateCallback(new Uri($"socyvia://oauth/cloudflare/callback?state={oauthRequest.State}&state={oauthRequest.State}&code=x"), oauthRequest).IsValid ||
    CloudflareDesktopOAuth.ValidateCallback(new Uri($"socyvia://oauth/cloudflare/callback?state={oauthRequest.State}&code=x&error=access_denied"), oauthRequest).IsValid ||
    CloudflareDesktopOAuth.ValidateCallback(new Uri($"socyvia://oauth/cloudflare/callback?state={oauthRequest.State}&code=x&unexpected=y"), oauthRequest).IsValid ||
    CloudflareDesktopOAuth.ValidateCallback(validCallback, oauthRequest, oauthRequest.CreatedAtUtc.AddMinutes(11)).IsValid)
    throw new Exception("OAuth callback validation did not reject an invalid origin, state, response, error, or expired request.");
var memoryCredentials = new MemoryCredentialStore();
var pendingStore = new CloudflareOAuthPendingStore(memoryCredentials);
await pendingStore.SaveAsync(oauthRequest);
if (await pendingStore.TakeAsync() is null || await pendingStore.TakeAsync() is not null)
    throw new Exception("OAuth state and PKCE verifier must be consumed exactly once.");
var oauthHandler = new OAuthTokenHandler();
var token = await new CloudflareOAuthProtocolClient(new HttpClient(oauthHandler)).ExchangeCodeAsync(oauthClient, "authorization-code", oauthRequest.CodeVerifier);
if (token.AccessToken != "access-value" || oauthHandler.LastForm is null ||
    oauthHandler.LastForm.Contains("client_secret", StringComparison.Ordinal) ||
    !oauthHandler.LastForm.Contains("code_verifier=", StringComparison.Ordinal))
    throw new Exception("OAuth token exchange must validate a bearer response and send PKCE without a client secret.");
var invalidCredentials = new MemoryCredentialStore();
await new CloudflareOAuthPendingStore(invalidCredentials).SaveAsync(oauthRequest);
var invalidTokenHandler = new OAuthTokenHandler();
var invalidConnection = new CloudflareOAuthConnectionService(
    invalidCredentials,
    new CloudflareOAuthProtocolClient(new HttpClient(invalidTokenHandler)),
    new CloudflareApiClient(new HttpClient(new CloudflareAccountHandler()) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") }));
var wrongStateResult = await invalidConnection.CompleteAsync(oauthClient, new Uri("socyvia://oauth/cloudflare/callback?state=wrong-state-value&code=must-not-exchange"));
var replayResult = await invalidConnection.CompleteAsync(oauthClient, validCallback);
if (wrongStateResult.Success || replayResult.Success || invalidTokenHandler.RequestCount != 0)
    throw new Exception("Wrong-state or replayed OAuth callbacks reached token exchange.");
var accountHandler = new CloudflareAccountHandler();
var discovered = await new CloudflareApiClient(new HttpClient(accountHandler) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") }).DiscoverAccountsAsync("access-value");
if (discovered.Count != 1 || discovered[0].Id != "account-id" || accountHandler.Authorization != "Bearer access-value")
    throw new Exception("OAuth account discovery did not use the in-memory bearer credential safely.");
var resourceHandler = new CloudflareResourceDiscoveryHandler();
var resources = await new CloudflareApiClient(new HttpClient(resourceHandler) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") })
    .DiscoverResearchResourcesAsync("account-id", "access-value");
if (resources.D1DatabaseId != "database-id" || resources.D1DatabaseName != "socyvia-research" ||
    resources.R2BucketName != "socyvia-experiments" || resources.WorkerName != "socyvia-runtime" ||
    resources.WorkerEndpoint != "https://socyvia-runtime.research-lab.workers.dev" || !resourceHandler.AllRequestsAuthorized)
    throw new Exception("OAuth resource discovery did not safely identify the existing D1, Worker, and optional R2 resources.");
var multiCredentials = new MemoryCredentialStore();
if (!CloudflareDesktopOAuth.TryCreateAuthorizationRequest(oauthClient, out var multiRequest) || multiRequest is null)
    throw new Exception("Multi-account OAuth fixture could not begin.");
await new CloudflareOAuthPendingStore(multiCredentials).SaveAsync(multiRequest);
var multiApiHandler = new MultiAccountCloudflareHandler();
var multiConnection = new CloudflareOAuthConnectionService(
    multiCredentials,
    new CloudflareOAuthProtocolClient(new HttpClient(new OAuthTokenHandler())),
    new CloudflareApiClient(new HttpClient(multiApiHandler) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") }));
var multiResult = await multiConnection.CompleteAsync(
    oauthClient,
    new Uri($"socyvia://oauth/cloudflare/callback?state={multiRequest.State}&code=one-time-code"),
    (accounts, _) => Task.FromResult<CloudflareOAuthAccount?>(accounts[1]));
if (!multiResult.Success || multiResult.Configuration?.AccountId != "account-two" ||
    await multiCredentials.RetrieveAsync(CloudflareOAuthConnectionService.TokenCredentialKey) is null)
    throw new Exception("OAuth multi-account selection did not persist the explicitly selected authorized account securely.");
var deployment = new ExperimentDeployment { DeploymentId = "deployment-fixture", ResearcherHandle = PublicExperimentLinkService.CreateResearcherHandle("Abdellah RIAD"), ExperimentCode = PublicExperimentLinkService.CreateResearchNumber("deployment-fixture"), Status = ExperimentDeploymentStatus.Published };
var published = PublicExperimentLinkService.ForPublishedDeployment(deployment);
if (published?.CanonicalUri.AbsoluteUri != $"https://socyvia.com/abdellah-riad/{deployment.ExperimentCode}" || PublicExperimentLinkService.ForPublishedDeployment(deployment with { Status = ExperimentDeploymentStatus.Draft }) is not null) throw new Exception("Canonical live links must be stable and only exist for published deployments.");
var payload = System.Text.Encoding.UTF8.GetBytes("release-artifact");
var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();
if (!await ReleaseArtifactIntegrityService.VerifySha256Async(new MemoryStream(payload), sha) || await ReleaseArtifactIntegrityService.VerifySha256Async(new MemoryStream(payload), new string('0', 64))) throw new Exception("Release SHA-256 verification must accept only the expected artifact.");
var offline = await new ReleaseUpdateCheckClient(new FixtureManifestVerifier(), new HttpClient(new OfflineHandler())).CheckAsync("1.0.0", "win-x64");
if (offline.State != ReleaseUpdateState.CheckFailed || !offline.Message!.Contains("offline", StringComparison.OrdinalIgnoreCase)) throw new Exception("Offline update checks must remain non-fatal.");
var reachable = await ConnectivityService.CheckAsync(new HttpClient(new ReachableHandler()), [new Uri("https://connectivity.fixture/health")]);
var unreachable = await ConnectivityService.CheckAsync(new HttpClient(new OfflineHandler()), [new Uri("https://connectivity.fixture/health")]);
var serviceUnavailable = await ConnectivityService.CheckAsync(new HttpClient(new RejectedConnectivityHandler()), [new Uri("https://connectivity.fixture/health")]);
if (reachable.State != ConnectivityState.Online || unreachable.State != ConnectivityState.Offline || serviceUnavailable.State != ConnectivityState.Online)
    throw new Exception("Connectivity status must require HTTPS reachability without confusing runtime health with internet availability.");
if (!(new ReleaseUpdateSafetyState(false, false, false, false)).CanOfferInstall || (new ReleaseUpdateSafetyState(true, false, false, false)).CanOfferInstall) throw new Exception("Updates must not interrupt active researcher work.");
if (!await ReleaseUpdateInstallGate.PrepareAsync(false, false, false) || await ReleaseUpdateInstallGate.PrepareAsync(true, false, false))
    throw new Exception("The update installation gate did not flush safe work or block an active publication.");
var saves = 0;
using (var coordinator = new StudySaveCoordinator(TimeSpan.FromMilliseconds(10)))
{
    coordinator.MarkDirty(_ => { saves++; return Task.CompletedTask; });
    if (coordinator.State != StudySaveState.UnsavedChanges || !await coordinator.FlushAsync() || coordinator.State != StudySaveState.Saved || saves != 1)
        throw new Exception("Autosave did not expose and flush the pending study state deterministically.");
    coordinator.MarkDirty(_ => throw new InvalidOperationException("fixture failure"));
    if (await coordinator.FlushAsync() || coordinator.State != StudySaveState.SaveFailed)
        throw new Exception("Autosave failure must block a scientifically unsafe transition.");
}
var conversationService = new AiConversationService();
var conversation = conversationService.New(dataset.StudyId, dataset.DatasetHash);
conversation = conversation with { Messages = [new AiConversationMessage("researcher", "Compare the groups.", DateTime.UtcNow)] };
await conversationService.SaveAsync(conversation);
var restoredConversation = await conversationService.LoadAsync(dataset.StudyId);
if (restoredConversation?.Messages.Single().Content != "Compare the groups." || !AiConversationService.IsAggregateSafe(request))
    throw new Exception("Study-scoped AI conversation context was not retained through the aggregate-safe boundary.");
var multiTurnMessages = new[]
{
    new AiConversationMessage("researcher", "Interpret the difference between Condition 1 and Condition 2.", DateTime.UtcNow),
    new AiConversationMessage("assistant", "The supplied deterministic evidence supports a group comparison.", DateTime.UtcNow),
    new AiConversationMessage("researcher", "What could explain this result?", DateTime.UtcNow),
    new AiConversationMessage("assistant", "Interpretive explanations require design-aware caution.", DateTime.UtcNow),
    new AiConversationMessage("researcher", "Write this as an academic Results paragraph.", DateTime.UtcNow),
    new AiConversationMessage("assistant", "The paragraph must retain the supplied numerical evidence.", DateTime.UtcNow),
    new AiConversationMessage("researcher", "What limitations should I report?", DateTime.UtcNow)
};
var followUpRequest = ResearchInterpretationService.BuildRequest(
    new Study { Id = dataset.StudyId, Title = "Fixture" }, dataset, quality, [result],
    prompt: multiTurnMessages[^1].Content, conversation: multiTurnMessages);
if (followUpRequest.Conversation?.Count != 7 || followUpRequest.Conversation[2].Content != "What could explain this result?" ||
    !AiConversationService.IsAggregateSafe(followUpRequest))
    throw new Exception("SOCYVIA AI did not preserve safe multi-turn study context.");
var managedAiReady = await new SocyviaAiGatewayClient(
    new SocyviaAiGatewayConfiguration(new Uri("https://ai.socyvia.com/")),
    new HttpClient(new AiGatewayStatusHandler(HttpStatusCode.OK, """{"service":"SOCYVIA AI","contractVersion":"SOCYVIA.AI/1","status":"ready"}"""))
    {
        BaseAddress = new Uri("https://ai.socyvia.com/")
    }).GetStatusAsync();
var managedAiFailure = await new SocyviaAiGatewayClient(
    new SocyviaAiGatewayConfiguration(new Uri("https://ai.socyvia.com/")),
    new HttpClient(new AiGatewayStatusHandler(HttpStatusCode.ServiceUnavailable, """{"status":"unavailable"}"""))
    {
        BaseAddress = new Uri("https://ai.socyvia.com/")
    }).GetStatusAsync();
var managedAiUnprovisioned = await new SocyviaAiGatewayClient(
    new SocyviaAiGatewayConfiguration(new Uri("https://ai.socyvia.com/")),
    new HttpClient(new AiGatewayStatusHandler(HttpStatusCode.OK, """{"service":"SOCYVIA AI","contractVersion":"SOCYVIA.AI/1","status":"unavailable","reason":"INFERENCE_NOT_PROVISIONED"}"""))
    {
        BaseAddress = new Uri("https://ai.socyvia.com/")
    }).GetStatusAsync();
if (managedAiReady.State != SocyviaAiServiceState.Ready || managedAiFailure.State != SocyviaAiServiceState.ServiceError ||
    managedAiUnprovisioned.State != SocyviaAiServiceState.Unavailable ||
    managedAiUnprovisioned.Reason != SocyviaAiServiceAvailabilityReason.GatewayNotConfigured)
    throw new Exception("SOCYVIA-managed AI service states were not mapped honestly.");
var managedAiInvalidContract = await new SocyviaAiGatewayClient(
    new SocyviaAiGatewayConfiguration(new Uri("https://ai.socyvia.com/")),
    new HttpClient(new AiGatewayStatusHandler(HttpStatusCode.OK, """{"status":"ready"}"""))
    {
        BaseAddress = new Uri("https://ai.socyvia.com/")
    }).GetStatusAsync();
if (managedAiInvalidContract.Reason != SocyviaAiServiceAvailabilityReason.InvalidContract)
    throw new Exception("SOCYVIA AI accepted an unidentified or incompatible gateway response.");
var aiInterpretationHandler = new AiGatewayInterpretationHandler();
var managedInterpretation = await new SocyviaAiGatewayClient(
    new SocyviaAiGatewayConfiguration(new Uri("https://ai.socyvia.com/")),
    new HttpClient(aiInterpretationHandler) { BaseAddress = new Uri("https://ai.socyvia.com/") },
    "desktop-oauth-token-value").InterpretAsync(request);
if (managedInterpretation.Status != ResearchInterpretationResponse.Generated ||
    managedInterpretation.Model != "openai/gpt-oss-120b" ||
    aiInterpretationHandler.Authorization != "Bearer desktop-oauth-token-value" ||
    aiInterpretationHandler.ContractVersion != SocyviaAiGatewayContract.Version ||
    aiInterpretationHandler.InputHash != SocyviaAiScientificGuardrails.InputHash(request) ||
    !aiInterpretationHandler.RequestWasAggregateOnly)
    throw new Exception("Desktop did not preserve the authenticated, aggregate-only SOCYVIA.AI/1 request/response contract.");
try
{
    await new SocyviaAiGatewayClient(
        new SocyviaAiGatewayConfiguration(new Uri("https://ai.socyvia.com/")),
        new HttpClient(new AiGatewayStatusHandler(HttpStatusCode.TooManyRequests, "{}")) { BaseAddress = new Uri("https://ai.socyvia.com/") })
        .InterpretAsync(request);
    throw new Exception("SOCYVIA AI did not expose a provider capacity limit.");
}
catch (SocyviaAiRateLimitException) { }
var managedConfiguration = SocyviaAiGatewayConfiguration.LoadManagedConfiguration();
if (managedConfiguration?.Endpoint != SocyviaAiGatewayConfiguration.ProductionEndpoint)
    throw new Exception("Desktop did not resolve the first-party SOCYVIA AI gateway boundary.");
var noEvidenceRequest = request with { EligibleN = 0, ResearcherPrompt = "Compare the groups." };
var noEvidence = await ResearchInterpretationService.InterpretAsync(noEvidenceRequest, new ThrowingAiProvider());
if (noEvidence.Status != ResearchInterpretationResponse.EvidenceUnavailable ||
    !noEvidence.Interpretation!.Contains("No participant evidence", StringComparison.Ordinal))
    throw new Exception("SOCYVIA AI did not stop before inference when n=0.");
var missingComparison = await ResearchInterpretationService.InterpretAsync(
    request with { Analyses = [], ResearcherPrompt = "Compare the groups." }, new ThrowingAiProvider());
var missingInference = await ResearchInterpretationService.InterpretAsync(
    request with { Analyses = [], ResearcherPrompt = "Explain this p-value." }, new ThrowingAiProvider());
if (missingComparison.Status != ResearchInterpretationResponse.EvidenceUnavailable ||
    missingInference.Status != ResearchInterpretationResponse.EvidenceUnavailable)
    throw new Exception("SOCYVIA AI invented an unavailable comparison or inferential result.");
var productApplicationState = new SocyviaAiApplicationState(
    "Publish", true, "Fixture", "Draft", true, true, true, false,
    "Media sources must be configured before publishing this experiment.", 0, 0, false);
var productHelp = ResearchInterpretationService.BuildProductHelpRequest(
    "Why is Publish disabled?", productApplicationState,
    [new AiConversationMessage("researcher", "What should I do next?", DateTime.UtcNow)]);
if (productHelp.AssistantMode != SocyviaAiAssistantModes.ProductHelp ||
    productHelp.ProductContext?.Topics.Any(topic => topic.Id == "publish") != true ||
    productHelp.ApplicationState?.PublishBlockingReason != productApplicationState.PublishBlockingReason ||
    SocyviaAiScientificGuardrails.Evaluate(productHelp) is not null ||
    !AiConversationService.IsAggregateSafe(productHelp))
    throw new Exception("Product help did not retain relevant, aggregate-safe SOCYVIA application context at n=0.");
var helpCases = new Dictionary<string, string>
{
    ["How do I create a new study?"] = "studies",
    ["How do I add a pre questionnaire?"] = "questionnaires",
    ["How do I connect Cloudflare?"] = "cloudflare",
    ["Where do I export the analysis report?"] = "analysis",
    ["ما الفرق بين الملف المحلي ومصدر الوسائط عند النشر؟"] = "media"
};
foreach (var helpCase in helpCases)
    if (!SocyviaAiProductKnowledgeService.SelectRelevant(helpCase.Key, helpCase.Key.Any(character => character is >= '\u0600' and <= '\u06ff'))
            .Topics.Any(topic => topic.Id == helpCase.Value))
        throw new Exception($"Product help topic routing failed for {helpCase.Value}.");
var latestRenderGate = new LatestUiRenderGate();
var renderedRoots = new List<string>();
var firstRender = latestRenderGate.RunAsync(async () =>
{
    renderedRoots.Clear(); await Task.Delay(25); renderedRoots.Add("old");
});
await Task.Delay(2);
var latestRender = latestRenderGate.RunAsync(async () =>
{
    renderedRoots.Clear(); await Task.Yield(); renderedRoots.Add("latest");
});
await Task.WhenAll(firstRender, latestRender);
if (renderedRoots.Count != 1 || renderedRoots[0] != "latest")
    throw new Exception("Latest UI render serialization can still leave duplicate workspace roots.");
if (StudyContextLabelService.ForDisplay("&&&&&&", true) != "دراسة بلا عنوان" ||
    StudyContextLabelService.ForDisplay("Research title", false) != "Research title")
    throw new Exception("Invalid stored study context can still render as placeholder garbage.");
#if DEBUG
var developmentInterpretation = await new SocyviaAiDevelopmentAdapter().InterpretAsync(request);
if (developmentInterpretation.Provider != "SOCYVIA AI — DEVELOPMENT ONLY" ||
    !developmentInterpretation.Interpretation!.StartsWith("DEVELOPMENT ONLY", StringComparison.Ordinal))
    throw new Exception("The test-only AI adapter was not unambiguously separated from production inference.");
#endif
var readyStudy = new ResearcherPublishReadiness([new ResearcherPublishCheck("Study", true, "Ready")]);
var textOnlyCloud = new CloudflareProviderConfiguration
{
    AccountId = "account",
    D1DatabaseId = "database",
    WorkerEndpoint = "https://runtime.example.test",
    R2BucketName = string.Empty,
    ConnectionMode = CloudflareConnectionMode.OAuth,
    ProviderStatus = CloudflareProviderConnectionState.Ready
};
var textOnlyPublication = PublicationReadinessService.Combine(
    readyStudy, textOnlyCloud,
    CloudMediaReadinessService.Evaluate([], textOnlyCloud));
var mediaRequiredPublication = PublicationReadinessService.Combine(
    readyStudy, textOnlyCloud,
    CloudMediaReadinessService.Evaluate([new MediaManifestAsset { RequiredForDeployment = true }], textOnlyCloud));
var mediaReadyCloud = textOnlyCloud with { R2BucketName = "researcher-media" };
var mediaReadyPublication = PublicationReadinessService.Combine(
    readyStudy, mediaReadyCloud,
    CloudMediaReadinessService.Evaluate([new MediaManifestAsset { RequiredForDeployment = true }], mediaReadyCloud));
if (!textOnlyPublication.CanPublish || textOnlyPublication.MediaRequired ||
    mediaRequiredPublication.CanPublish || !mediaRequiredPublication.MediaRequired ||
    mediaRequiredPublication.Media.State != CloudMediaReadinessState.RemoteMediaUrlMissing ||
    !mediaReadyPublication.CanPublish)
    throw new Exception("The authoritative publication readiness contract mishandled text-only or media requirements.");
foreach (var mediaType in new[] { "Image", "Video", "Audio" })
{
    var localMedia = CloudMediaReadinessService.Evaluate(
        [new MediaManifestAsset { MediaType = mediaType, RequiredForDeployment = true }], textOnlyCloud);
    if (localMedia.CanPublish || localMedia.RequiredAssetCount != 1)
        throw new Exception($"Local {mediaType} media did not require a remote publication URL.");

    var externalSnapshot = new SnapshotStimulus
    {
        StimulusId = "external-" + mediaType.ToLowerInvariant(),
        ContentItemId = "content-" + mediaType.ToLowerInvariant(),
        ContentType = mediaType,
        MediaPath = $"C:\\local-preview\\asset.{(mediaType == "Image" ? "png" : mediaType == "Video" ? "mp4" : "mp3")}",
        PublishedMediaUrl = $"https://socyvia.com/experimentfeed/demo-media/{(mediaType == "Image" ? "sport-supporters.jpg" : mediaType == "Video" ? "socyvia-demo-video.mp4" : "socyvia-demo-tone.mp3")}" 
    };
    var externalManifest = RemoteExperimentFoundationService.BuildMediaManifest([externalSnapshot]);
    var externalReadiness = CloudMediaReadinessService.Evaluate(externalManifest, textOnlyCloud);
    if (externalManifest.Count != 1 || externalManifest[0].RequiredForDeployment ||
        externalManifest[0].DeploymentUrl != externalSnapshot.PublishedMediaUrl ||
        externalReadiness.State != CloudMediaReadinessState.Ready || !externalReadiness.CanPublish)
        throw new Exception($"Valid external HTTPS {mediaType} media did not publish without R2.");
}
var externalMedia = CloudMediaReadinessService.Evaluate(
    [new MediaManifestAsset { MediaType = "Video", OriginalSourceType = "ExternalUrl", DeploymentUrl = "https://socyvia.com/experimentfeed/demo-media/socyvia-demo-video.mp4", RequiredForDeployment = false }], textOnlyCloud);
if (!externalMedia.CanPublish || externalMedia.RequiredAssetCount != 0)
    throw new Exception("Supported externally hosted media was unnecessarily blocked by local media storage readiness.");
if (!PublishedMediaUrlValidator.TryValidateDirectMedia("https://socyvia.com/experimentfeed/demo-media/sport-supporters.jpg", out _, out _) ||
    PublishedMediaUrlValidator.TryValidate("file:///C:/media/image.png", out _, out _) ||
    PublishedMediaUrlValidator.TryValidate("https://localhost/image.png", out _, out _) ||
    PublishedMediaUrlValidator.TryValidate("https://127.0.0.1/image.png", out _, out _) ||
    PublishedMediaUrlValidator.TryValidate("https://192.168.1.20/image.png", out _, out _) ||
    PublishedMediaUrlValidator.TryValidate("https://media.example.test/image.png", out _, out _) ||
    PublishedMediaUrlValidator.TryValidate("https://media.example.com/image.png", out _, out _) ||
    !PublishedMediaUrlValidator.TryValidate("https://www.youtube.com/watch?v=fixture", out var socialPage, out _) ||
    !PublishedMediaUrlValidator.IsExternalContentPage(socialPage!) ||
    PublishedMediaUrlValidator.TryValidateDirectMedia("https://www.youtube.com/watch?v=fixture", out _, out _))
    throw new Exception("Published media URL validation accepted a local/private source or rejected public HTTPS.");
var publishedStatus = new PublishedExperimentStatus("study", "deployment", "https://socyvia.com/researcher/12345678", "https://runtime.example.test/experimentfeed/researcher/12345678", 1, DateTime.UtcNow, PublicExperimentLinkService.LegacyPreparedRoutingStatus);
var livePublishedStatus = publishedStatus with { RoutingStatus = PublicExperimentLinkService.LiveRoutingStatus };
if (PublicationWorkspaceStateService.Resolve(textOnlyPublication, null, false, false, null) != PublicationWorkspaceState.Ready ||
    PublicationWorkspaceStateService.Resolve(mediaRequiredPublication, null, false, false, null) != PublicationWorkspaceState.NotReady ||
    PublicationWorkspaceStateService.Resolve(textOnlyPublication, null, false, true, null) != PublicationWorkspaceState.Publishing ||
    PublicationWorkspaceStateService.Resolve(textOnlyPublication, null, false, false, "failure") != PublicationWorkspaceState.Failed ||
    PublicationWorkspaceStateService.Resolve(textOnlyPublication, publishedStatus, true, false, null) != PublicationWorkspaceState.PublishedAwaitingCanonicalRoute ||
    PublicationWorkspaceStateService.Resolve(textOnlyPublication, livePublishedStatus, true, false, null) != PublicationWorkspaceState.Published ||
    PublicationWorkspaceStateService.Resolve(textOnlyPublication, publishedStatus, false, false, null) != PublicationWorkspaceState.Ready)
    throw new Exception("Publish not-ready/ready/publishing/failure/success/configuration-change states diverged.");
if (PublicExperimentLinkService.DistributableCanonicalUri(publishedStatus) is not null ||
    PublicExperimentLinkService.DistributableCanonicalUri(livePublishedStatus)?.AbsoluteUri != publishedStatus.CanonicalParticipantUrl ||
    PublicExperimentLinkService.DistributableCanonicalUri(livePublishedStatus with { CanonicalParticipantUrl = "https://example.org/wrong" }) is not null)
    throw new Exception("Copy/Open participant actions did not require a verified live socyvia.com canonical route.");
var physiology = FuturePhysiologyPresentationService.Cards(true);
if (physiology[0].Measurement != "التخطيط الكهربائي للدماغ (EEG)" || physiology[0].Ecosystem != "OpenBCI" || physiology[1].Measurement != "الاستجابة الجلدية الكهربائية (GSR / EDA)" || physiology[1].Ecosystem != "EmotiBit" || physiology[2].Measurement != "تتبع العين (Eye Tracking)" || physiology[2].Ecosystem != "Pupil Labs") throw new Exception("Future physiology labels must retain approved scientific and ecosystem terminology.");
if (ResearchVariableDisplayService.Label(new AnalysisVariable { Id = "session_duration_ms" }) != "Session duration") throw new Exception("Researcher-facing variable labels must not expose storage identifiers.");
if (SocyviaProductIdentity.EnglishPositioning != "Scientific Testing for Computational Social Science" ||
    SocyviaProductIdentity.ArabicPositioning != "الاختبار العلمي في العلوم الاجتماعية الحاسوبية")
    throw new Exception("The final bilingual SOCYVIA positioning changed.");
var repositoryRoot = FindRepositoryRoot();
var localizedProductFiles = new[]
    {
        Path.Combine(repositoryRoot, "Views"),
        Path.Combine(repositoryRoot, "Services"),
        Path.Combine(repositoryRoot, "Models")
    }
    .Where(Directory.Exists)
    .SelectMany(path => Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
    .Concat(Directory.EnumerateFiles(repositoryRoot, "*.*", SearchOption.TopDirectoryOnly))
    .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
var forbiddenArabicProductSpellings = new[]
{
    "اسيلة", "اسئله", "نتايج", "الوسايط", "حجم الاثر", "ماذا علي ان افعل الان"
};
foreach (var localizedProductFile in localizedProductFiles)
{
    var localizedProductText = await File.ReadAllTextAsync(localizedProductFile);
    var forbiddenSpelling = forbiddenArabicProductSpellings.FirstOrDefault(localizedProductText.Contains);
    if (forbiddenSpelling is not null)
        throw new Exception($"Arabic localization regression '{forbiddenSpelling}' found in {Path.GetRelativePath(repositoryRoot, localizedProductFile)}.");
}
var aiRuntimeArabic = SocyviaAiUiCopy.AllArabicRuntimeStrings;
if (aiRuntimeArabic.Count < 17 ||
    aiRuntimeArabic.Any(SocyviaAiUiCopy.ContainsMalformedArabic) ||
    !aiRuntimeArabic.Contains("أسئلة مقترحة", StringComparer.Ordinal) ||
    !aiRuntimeArabic.Contains("ماذا علي أن أفعل الآن؟", StringComparer.Ordinal) ||
    !aiRuntimeArabic.Contains("اشرح حجم الأثر", StringComparer.Ordinal) ||
    !aiRuntimeArabic.Contains("اكتب فقرة نتائج", StringComparer.Ordinal))
    throw new Exception("The actual SOCYVIA AI runtime copy catalog contains malformed or incomplete Arabic.");
var renderedArabicRuntimeCopy = new[]
{
    "أسئلة مقترحة",
    "ماذا علي أن أفعل الآن؟",
    "اشرح حجم الأثر",
    "اكتب فقرة نتائج",
    "مصدر الوسائط عند النشر"
}.Select(UiTextService.Arabic).ToArray();
if (!renderedArabicRuntimeCopy.SequenceEqual(new[]
    {
        "أسئلة مقترحة",
        "ماذا علي أن أفعل الآن؟",
        "اشرح حجم الأثر",
        "اكتب فقرة نتائج",
        "مصدر الوسائط عند النشر"
    }, StringComparer.Ordinal))
    throw new Exception("Arabic runtime normalization removed an orthographic hamza/madda or altered approved UI copy.");
if (UiTextService.Arabic("نَتَائِجُ البَحْثِ.") != "نتائج البحث")
    throw new Exception("Arabic runtime normalization must remove optional tashkeel without damaging letters.");
var identityFiles = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
    .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                   !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                   !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") &&
                   Path.GetExtension(path) is ".cs" or ".axaml" or ".md" or ".csproj");
foreach (var file in identityFiles)
{
    var sourceText = await File.ReadAllTextAsync(file);
    var obsoleteDigital = "Digital " + "Experimentation for Computational Social Science";
    var obsoleteScientific = "Scientific " + "Experimentation for Computational Social Science";
    var obsoleteArabic = "التجريب" + " العلمي في العلوم الاجتماعية الحاسوبية";
    if (sourceText.Contains(obsoleteDigital, StringComparison.Ordinal) ||
        sourceText.Contains(obsoleteScientific, StringComparison.Ordinal) ||
        sourceText.Contains(obsoleteArabic, StringComparison.Ordinal))
        throw new Exception($"Obsolete product positioning remains in {Path.GetRelativePath(repositoryRoot, file)}.");
}
var dashboardXaml = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "DashboardView.axaml"));
var dashboardCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "DashboardView.axaml.cs"));
var themeXaml = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Styles", "SocyviaTheme.axaml"));
if (!themeXaml.Contains("SocyviaGlassSubtleBrush", StringComparison.Ordinal) ||
    !themeXaml.Contains("SocyviaGlassStandardBrush", StringComparison.Ordinal) ||
    !themeXaml.Contains("SocyviaGlassElevatedBrush", StringComparison.Ordinal) ||
    !themeXaml.Contains("SocyviaGlassFloatingBrush", StringComparison.Ordinal) ||
    !themeXaml.Contains("Button.actionCard", StringComparison.Ordinal) ||
    !themeXaml.Contains("SocyviaPageTitleSize", StringComparison.Ordinal))
    throw new Exception("The centralized Scientific Glass Instrument token hierarchy regressed.");
if (!dashboardXaml.Contains("x:Name=\"GlobalHeaderGrid\"", StringComparison.Ordinal) ||
    !dashboardXaml.Contains("Grid.ColumnSpan=\"2\"", StringComparison.Ordinal) ||
    !dashboardCode.Contains("Grid.SetColumn(ProfileButton, 1)", StringComparison.Ordinal) ||
    !dashboardCode.Contains("Grid.SetColumn(ProfileButton, 0)", StringComparison.Ordinal))
    throw new Exception("Global header geometry no longer preserves physical mirroring and a true centered connection badge.");
if (!dashboardXaml.Contains("HorizontalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal) ||
    !dashboardXaml.Contains("TextWrapping=\"NoWrap\"", StringComparison.Ordinal) ||
    !dashboardXaml.Contains("RowDefinitions=\"58,*,50\"", StringComparison.Ordinal) ||
    !dashboardXaml.Contains("MinHeight=\"50\"", StringComparison.Ordinal) ||
    !dashboardCode.Contains("OpenSocyviaAiAsync", StringComparison.Ordinal) ||
    !dashboardCode.Contains("ShowSocyviaAiProductHelp();", StringComparison.Ordinal) ||
    !dashboardCode.Contains("Public Demo", StringComparison.Ordinal) ||
    dashboardCode.Contains("Coming later. No AI service", StringComparison.Ordinal))
    throw new Exception("Footer single-line behavior or the study-scoped SOCYVIA AI route regressed.");
var resultsWorkspaceCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "ResearchResultsWorkspaceView.cs"));
if (!resultsWorkspaceCode.Contains("What SOCYVIA AI knows", StringComparison.Ordinal) ||
    !resultsWorkspaceCode.Contains("Current Evidence", StringComparison.Ordinal) ||
    !resultsWorkspaceCode.Contains("Suggested questions", StringComparison.Ordinal) ||
    resultsWorkspaceCode.Contains("Open AI Settings", StringComparison.Ordinal) ||
    resultsWorkspaceCode.Contains("Connect an AI provider", StringComparison.Ordinal) ||
    !resultsWorkspaceCode.Contains("Grid.SetColumn(contextSurface, ar ? 0 : 2)", StringComparison.Ordinal) ||
    !resultsWorkspaceCode.Contains("Grid.SetColumn(conversationSurface, ar ? 2 : 0)", StringComparison.Ordinal) ||
    resultsWorkspaceCode.Split("Name = \"SocyviaAiWorkspaceRoot\"").Length - 1 != 1 ||
    resultsWorkspaceCode.Split("Name = \"SocyviaAiComposer\"").Length - 1 != 1 ||
    resultsWorkspaceCode.Split("Name = \"SocyviaAiSuggestedPrompts\"").Length - 1 != 1 ||
    !resultsWorkspaceCode.Contains("LatestUiRenderGate", StringComparison.Ordinal) ||
    !resultsWorkspaceCode.Contains("LocalizationService.LanguageChanged += OnLanguageChanged", StringComparison.Ordinal))
    throw new Exception("SOCYVIA AI context, evidence, managed-service state, or physical RTL/LTR mirror regressed.");
var productHelpViewCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "SocyviaAiProductHelpView.cs"));
if (productHelpViewCode.Split("Name = \"SocyviaAiProductHelpWorkspaceRoot\"").Length - 1 != 1 ||
    productHelpViewCode.Split("Name = \"SocyviaAiProductComposer\"").Length - 1 != 1 ||
    !productHelpViewCode.Contains("SocyviaAiUiCopy.ProductHelpPrompts", StringComparison.Ordinal) ||
    !resultsWorkspaceCode.Contains("SocyviaAiUiCopy.StudyPrompts", StringComparison.Ordinal) ||
    !dashboardCode.Contains("ShowSocyviaAiProductHelp()", StringComparison.Ordinal) ||
    !dashboardCode.Contains("if (_socyviaAiProductHelpView is not null) ShowSocyviaAiProductHelp();", StringComparison.Ordinal))
    throw new Exception("Global SOCYVIA AI product-help navigation, language refresh, or single-root contract regressed.");
if (dashboardXaml.Contains("AiApiKeyBox", StringComparison.Ordinal) ||
    dashboardXaml.Contains("AiModelBox", StringComparison.Ordinal) ||
    dashboardCode.Contains("AiProviderConfigurationStore", StringComparison.Ordinal) ||
    dashboardCode.Contains("Groq", StringComparison.Ordinal))
    throw new Exception("Researcher-facing SOCYVIA AI settings exposed provider, model, or credential configuration.");
var studyWorkspaceXaml = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "StudyWorkspaceView.axaml"));
var studyWorkspaceCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "StudyWorkspaceView.axaml.cs"));
var normalizedStudyWorkspaceCode = studyWorkspaceCode.Replace("\r\n", "\n", StringComparison.Ordinal);
if (!studyWorkspaceXaml.Contains("x:Name=\"ParticipantFlowStages\" Columns=\"4\" Rows=\"2\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("FlowFeedText.Text = ar ? \"خلاصة SOCYVIA\" : \"SOCYVIA Feed\"", StringComparison.Ordinal))
    throw new Exception("The participant protocol rail no longer protects the Experiment Feed stage from overflow.");
if (!studyWorkspaceXaml.Contains("Classes=\"quickAction acquisitionAction\"", StringComparison.Ordinal) ||
    studyWorkspaceXaml.Split("Classes=\"quickAction acquisitionAction\"").Length - 1 != 3 ||
    studyWorkspaceXaml.Split("Classes=\"acquisitionIcon\"").Length - 1 != 3 ||
    !studyWorkspaceXaml.Contains("x:Name=\"CreateManualButton\" Grid.Column=\"0\" Width=\"220\"", StringComparison.Ordinal) ||
    !studyWorkspaceXaml.Contains("x:Name=\"FromDeviceButton\" Grid.Column=\"1\" Width=\"220\"", StringComparison.Ordinal) ||
    !studyWorkspaceXaml.Contains("x:Name=\"FromUrlButton\" Grid.Column=\"2\" Width=\"220\"", StringComparison.Ordinal) ||
    !studyWorkspaceXaml.Contains("x:Name=\"CreateManualTitle\" Text=\"Add Content\"", StringComparison.Ordinal) ||
    !studyWorkspaceXaml.Contains("x:Name=\"FromDeviceTitle\" Text=\"Add Media\"", StringComparison.Ordinal) ||
    !studyWorkspaceXaml.Contains("x:Name=\"FromUrlTitle\" Text=\"Add External Link\"", StringComparison.Ordinal) ||
    !studyWorkspaceXaml.Contains("Text=\"Text, post or experimental material\"", StringComparison.Ordinal) ||
    !studyWorkspaceXaml.Contains("Text=\"Image, video or audio\"", StringComparison.Ordinal) ||
    !studyWorkspaceXaml.Contains("Text=\"Externally hosted source or content\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("\"إضافة محتوى\" : \"Add Content\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("\"إضافة وسائط\" : \"Add Media\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("\"إضافة رابط خارجي\" : \"Add External Link\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("? \"نص، منشور أو مادة تجريبية\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("? \"صورة، فيديو أو صوت\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("? \"مصدر أو محتوى مستضاف خارجيا\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("Text, post or experimental material", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("Image, video or audio", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("Externally hosted source or content", StringComparison.Ordinal) ||
    studyWorkspaceXaml.Contains("quickActionIconHost", StringComparison.Ordinal) ||
    studyWorkspaceXaml.Contains("Text=\"→\"", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("CreateManualButton.Click +=", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("FromDeviceButton.Click +=", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("FromUrlButton.Click +=", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("LocalizationService.LanguageChanged += OnWorkspaceLanguageChanged", StringComparison.Ordinal) ||
    !normalizedStudyWorkspaceCode.Contains("ConfigureParticipantFlow();\n        ConfigureAcquisitionLanguage();", StringComparison.Ordinal))
    throw new Exception("Content & Media text-led action cards or their existing commands regressed.");
var publishWorkspaceCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "PublishWorkspaceView.cs"));
if (!publishWorkspaceCode.Contains("PublicationReadinessService.EvaluateAsync", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("publish.IsEnabled = readiness.CanPublish", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("published.ConfigurationHash, preparedTask.Result.Package.ConfigurationHash", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("Publishing experiment...", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("ParticipantLinkActionService.CopyAsync", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("open.Click += (_, _) => ParticipantLinkActionService.Open(published)", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("CANONICAL ROUTE NOT LIVE", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("PublicationWorkspaceStateService.Resolve", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("A local research package was saved.", StringComparison.Ordinal) ||
    publishWorkspaceCode.Contains("Research Package exported:", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("_ => PublishReadyCard(readiness)", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("_body.Children.Add(actionPanel)", StringComparison.Ordinal) ||
    publishWorkspaceCode.IndexOf("_body.Children.Add(actionPanel)", StringComparison.Ordinal) >
    publishWorkspaceCode.IndexOf("_body.Children.Add(ReadinessCard", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("Media sources must be configured before publishing this experiment", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("يجب إعداد مصادر الوسائط للنشر قبل نشر هذه التجربة", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("Set media URLs", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("MediaUrlsSetupRequested", StringComparison.Ordinal) ||
    publishWorkspaceCode.Contains("Set up media storage", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("if (_isPublishing) return;", StringComparison.Ordinal) ||
    !publishWorkspaceCode.Contains("retry.Click += async (_, _) => await PublishAsync(retry)", StringComparison.Ordinal) ||
    publishWorkspaceCode.Contains("StimulusPostRepository.GetByStudyAsync(_study.Id)", StringComparison.Ordinal))
    throw new Exception("Publish UI no longer derives every visible state and action from the prepared-package readiness result.");
var contentLibraryXaml = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "ContentLibraryView.axaml"));
var contentLibraryCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "ContentLibraryView.axaml.cs"));
if (!contentLibraryXaml.Contains("x:Name=\"PublishedMediaUrlBox\"", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("PublishedMediaUrlValidator.TryValidate", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("مصدر الوسائط عند النشر", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("Published media source", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("يجب أن يكون الرابط متاحا للمشاركين دون تسجيل دخول.", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("The URL must be accessible to participants without signing in.", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("PublishedMediaUrlValidator.TryValidateDirectMedia", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("Use Add External Link instead of a direct media source.", StringComparison.Ordinal))
    throw new Exception("Content & Media does not persist and validate the bilingual participant-facing media URL.");
if (contentLibraryCode.Contains("مصدر الوسايط عند النشر", StringComparison.Ordinal) ||
    studyWorkspaceCode.Contains("مصدر الوسايط عند النشر", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("مصدر الوسائط عند النشر", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("مصدر الوسائط عند النشر", StringComparison.Ordinal))
    throw new Exception("The published-media Arabic label spelling regressed.");
if (!studyWorkspaceCode.Contains("MediaUrlsSetupRequested +=", StringComparison.Ordinal) ||
    !studyWorkspaceCode.Contains("contentItemId => MediaUrlsSetupRequested?.Invoke(contentItemId)", StringComparison.Ordinal) ||
    !dashboardCode.Contains("ShowContentLibrary(contentItemId)", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("OpenInitialContentItemAsync", StringComparison.Ordinal) ||
    !contentLibraryCode.Contains("PublishedMediaUrlBox.Focus()", StringComparison.Ordinal))
    throw new Exception("Publish media remediation does not open the existing Content & Media configuration workflow.");
if (!dashboardXaml.Contains("<WrapPanel HorizontalAlignment=\"Stretch\">", StringComparison.Ordinal) ||
    !dashboardXaml.Contains("x:Name=\"CloudOptionalMediaDisclosure\"", StringComparison.Ordinal) ||
    dashboardXaml.Contains("x:Name=\"CloudMediaSetupButton\" Grid.Column=", StringComparison.Ordinal) ||
    !dashboardCode.Contains("usage-based billing", StringComparison.Ordinal))
    throw new Exception("Cloudflare Settings actions can overflow or optional R2 billing is not disclosed in Advanced settings.");
var onboardingXaml = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "OnboardingTourWindow.axaml"));
if (!themeXaml.Contains("SocyviaRoleAiSurfaceBrush", StringComparison.Ordinal) ||
    !themeXaml.Contains("SocyviaRoleAttentionSurfaceBrush", StringComparison.Ordinal) ||
    !themeXaml.Contains("SocyviaRoleNeutralSurfaceBrush", StringComparison.Ordinal) ||
    !onboardingXaml.Contains("SocyviaRoleNeutralSurfaceBrush", StringComparison.Ordinal) ||
    !onboardingXaml.Contains("SocyviaRolePrimarySurfaceBrush", StringComparison.Ordinal))
    throw new Exception("Semantic role resources or first-launch compatibility regressed.");
var mainWindowXaml = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "MainWindow.axaml"));
var mainWindowCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "MainWindow.axaml.cs"));
var windowChromeXaml = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "SocyviaWindowChrome.axaml"));
var windowChromeCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "SocyviaWindowChrome.axaml.cs"));
var loginXaml = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "LoginView.axaml"));
var loginCode = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Views", "LoginView.axaml.cs"));
var navigationIdentityCount = dashboardXaml.Split("Classes=\"navIdentity\"", StringSplitOptions.None).Length - 1;
if (navigationIdentityCount != 10 ||
    dashboardXaml.Contains("ColumnDefinitions=\"28,*\"", StringComparison.Ordinal) ||
    !dashboardCode.Contains("SetArabicCenterText(ResearchNavigationLabel", StringComparison.Ordinal) ||
    !dashboardCode.Contains("SetArabicCenterText(IntelligenceNavigationLabel", StringComparison.Ordinal) ||
    !dashboardCode.Contains("SetArabicCenterText(SystemNavigationLabel", StringComparison.Ordinal) ||
    !dashboardXaml.Contains("Classes=\"nav demo\"", StringComparison.Ordinal) ||
    !dashboardXaml.Contains("Value=\"0,4,0,16\"", StringComparison.Ordinal))
    throw new Exception("The shared RTL sidebar identity geometry, centered headings, or Demo separation regressed.");
if (!mainWindowXaml.Contains("x:Name=\"ApplicationWindowChrome\"", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("ApplicationWindowChrome.Attach", StringComparison.Ordinal) ||
    !windowChromeXaml.Contains("ElementRole=\"TitleBar\"", StringComparison.Ordinal) ||
    !windowChromeXaml.Contains("ElementRole=\"MinimizeButton\"", StringComparison.Ordinal) ||
    !windowChromeXaml.Contains("ElementRole=\"MaximizeButton\"", StringComparison.Ordinal) ||
    !windowChromeXaml.Contains("ElementRole=\"CloseButton\"", StringComparison.Ordinal) ||
    !windowChromeCode.Contains("OperatingSystem.IsMacOS()", StringComparison.Ordinal) ||
    !windowChromeCode.Contains("WindowDecorations.BorderOnly", StringComparison.Ordinal) ||
    !windowChromeCode.Contains("WindowDecorations.Full", StringComparison.Ordinal) ||
    !windowChromeCode.Contains("ExtendClientAreaToDecorationsHint = true", StringComparison.Ordinal))
    throw new Exception("The cross-platform SOCYVIA window-chrome contract regressed.");
if (!loginXaml.Contains("RowDefinitions=\"54,10,40,10,82,10,60,10,60,*,44,10,40\"", StringComparison.Ordinal) ||
    !loginXaml.Contains("x:Name=\"ResearcherWorkspaceCard\"", StringComparison.Ordinal) ||
    !loginXaml.Contains("x:Name=\"ResearcherWorkspaceForm\"", StringComparison.Ordinal) ||
    !loginXaml.Contains("<Setter Property=\"Width\" Value=\"150\"/>", StringComparison.Ordinal) ||
    loginXaml.Contains("VerticalAlignment=\"Center\" MinHeight=\"520\"", StringComparison.Ordinal) ||
    loginXaml.Split("x:Name=\"EnterWorkspaceButton\"", StringSplitOptions.None).Length - 1 != 1 ||
    !loginCode.Contains("BrandSubtitleText.FontSize = 12;", StringComparison.Ordinal) ||
    !loginCode.Contains("HeroLine1Text.FontSize = 10.8;", StringComparison.Ordinal))
    throw new Exception("The shared Arabic/English Researcher Workspace geometry or minimal English brand-copy balance regressed.");
Console.WriteLine("20/20 analytics, OAuth, autosave, product-help AI, connectivity, identity, RTL/LTR, login mirror, shell chrome, update, public-link, and UTF-8 gates passed");

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "SOCYVIA.csproj"))) current = current.Parent;
    return current?.FullName ?? throw new DirectoryNotFoundException("SOCYVIA repository root was not found.");
}

sealed class FixtureManifestVerifier : IReleaseManifestVerifier
{
    public bool Verify(string canonicalManifestJson, string keyId, string signature) => keyId == "fixture" && signature == "signature";
}

sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("Offline fixture");
}

sealed class ReachableHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
}

sealed class RejectedConnectivityHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
}

sealed class MemoryCredentialStore : ISecureCredentialStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    public Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default) { _values[key] = secret; return Task.CompletedTask; }
    public Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.GetValueOrDefault(key));
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) { _values.Remove(key); return Task.CompletedTask; }
}

sealed class OAuthTokenHandler : HttpMessageHandler
{
    public string? LastForm { get; private set; }
    public int RequestCount { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        LastForm = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"access-value\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"refresh_token\":\"refresh-value\"}")
        };
    }
}

sealed class MultiAccountCloudflareHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? string.Empty;
        var json = path switch
        {
            var value when value.StartsWith("/client/v4/accounts?", StringComparison.Ordinal) => "{\"success\":true,\"result\":[{\"id\":\"account-one\",\"name\":\"First Research Account\"},{\"id\":\"account-two\",\"name\":\"Second Research Account\"}]}",
            var value when value.Contains("/d1/database", StringComparison.Ordinal) => "{\"success\":true,\"result\":[{\"uuid\":\"database-id\",\"name\":\"socyvia-research\"}]}",
            var value when value.EndsWith("/workers/scripts", StringComparison.Ordinal) => "{\"success\":true,\"result\":[{\"id\":\"socyvia-runtime\"}]}",
            var value when value.Contains("/workers/scripts/socyvia-runtime/subdomain", StringComparison.Ordinal) => "{\"success\":true,\"result\":{\"enabled\":true}}",
            var value when value.EndsWith("/workers/subdomain", StringComparison.Ordinal) => "{\"success\":true,\"result\":{\"subdomain\":\"research-lab\"}}",
            var value when value.Contains("/r2/buckets", StringComparison.Ordinal) => "{\"success\":true,\"result\":{\"buckets\":[]}}",
            _ => "{\"success\":false,\"result\":null}"
        };
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}

sealed class CloudflareAccountHandler : HttpMessageHandler
{
    public string? Authorization { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Authorization = request.Headers.Authorization?.ToString();
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\":true,\"result\":[{\"id\":\"account-id\",\"name\":\"Research Account\"}]}")
        });
    }
}

sealed class CloudflareResourceDiscoveryHandler : HttpMessageHandler
{
    public bool AllRequestsAuthorized { get; private set; } = true;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AllRequestsAuthorized &= request.Headers.Authorization?.ToString() == "Bearer access-value";
        var path = request.RequestUri?.PathAndQuery ?? string.Empty;
        var json = path switch
        {
            var value when value.Contains("/d1/database", StringComparison.Ordinal) => "{\"success\":true,\"result\":[{\"uuid\":\"database-id\",\"name\":\"socyvia-research\"}]}",
            var value when value.EndsWith("/workers/scripts", StringComparison.Ordinal) => "{\"success\":true,\"result\":[{\"id\":\"socyvia-runtime\"}]}",
            var value when value.Contains("/workers/scripts/socyvia-runtime/subdomain", StringComparison.Ordinal) => "{\"success\":true,\"result\":{\"enabled\":true}}",
            var value when value.EndsWith("/workers/subdomain", StringComparison.Ordinal) => "{\"success\":true,\"result\":{\"subdomain\":\"research-lab\"}}",
            var value when value.Contains("/r2/buckets", StringComparison.Ordinal) => "{\"success\":true,\"result\":{\"buckets\":[{\"name\":\"socyvia-experiments\"}]}}",
            _ => "{\"success\":false,\"result\":null}"
        };
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}

sealed class AiFailureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
}

sealed class AiGatewayStatusHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
}

sealed class AiGatewayInterpretationHandler : HttpMessageHandler
{
    public string? Authorization { get; private set; }
    public string? ContractVersion { get; private set; }
    public string? InputHash { get; private set; }
    public bool RequestWasAggregateOnly { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Authorization = request.Headers.Authorization?.ToString();
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        ContractVersion = document.RootElement.GetProperty("contractVersion").GetString();
        InputHash = document.RootElement.GetProperty("inputHash").GetString();
        var evidence = document.RootElement.GetProperty("request").GetRawText();
        RequestWasAggregateOnly = !evidence.Contains("ParticipantId", StringComparison.OrdinalIgnoreCase) &&
                                  !evidence.Contains("AccessToken", StringComparison.OrdinalIgnoreCase) &&
                                  !evidence.Contains("RawValue", StringComparison.OrdinalIgnoreCase);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                contractVersion = SocyviaAiGatewayContract.Version,
                status = "generated",
                model = "openai/gpt-oss-120b",
                interpretation = "The supplied deterministic evidence was interpreted without recalculation.",
                inputHash = InputHash,
                generatedAtUtc = DateTime.UtcNow,
                safetyNotes = new[] { "Researcher review required." }
            }), System.Text.Encoding.UTF8, "application/json")
        };
    }
}

sealed class ThrowingAiProvider : IResearchInterpretationProvider
{
    public string ProviderName => "must-not-run";
    public Task<ResearchInterpretationResponse> InterpretAsync(ResearchInterpretationRequest request, CancellationToken cancellationToken = default) =>
        throw new Exception("Scientific guardrails did not stop an evidence-invalid request before inference.");
}
