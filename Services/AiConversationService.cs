using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>Local study-scoped conversation persistence; excluded from Research Packages and never stores credentials.</summary>
public sealed class AiConversationService
{
    private static string Folder => Path.Combine(StorageService.SettingsFolder, "ai-conversations");

    public async Task<AiStudyConversation> GetOrCreateAsync(string studyId, string datasetHash, CancellationToken cancellationToken = default)
    {
        var existing = await LoadAsync(studyId, cancellationToken);
        return existing ?? New(studyId, datasetHash);
    }

    public async Task<AiStudyConversation?> LoadAsync(string studyId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(studyId);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AiStudyConversation>(stream, cancellationToken: cancellationToken);
    }

    public async Task SaveAsync(AiStudyConversation conversation, CancellationToken cancellationToken = default)
    {
        Validate(conversation);
        Directory.CreateDirectory(Folder);
        await using var stream = File.Create(PathFor(conversation.StudyId));
        await JsonSerializer.SerializeAsync(stream, conversation, cancellationToken: cancellationToken);
    }

    public AiStudyConversation New(string studyId, string datasetHash) =>
        new(Guid.NewGuid().ToString(), studyId, datasetHash, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<AiConversationMessage>());

    public async Task ClearAsync(string studyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathFor(studyId);
        if (File.Exists(path)) File.Delete(path);
        await Task.CompletedTask;
    }

    public static string ContextHash(ResearchInterpretationRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request with
        {
            ResearcherPrompt = null,
            Conversation = null
        })))).ToLowerInvariant();

    public static bool IsAggregateSafe(ResearchInterpretationRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        var prohibited = new[] { "ParticipantId", "ParticipantCode", "ResponseJson", "RawValue", "ApiKey", "AccessToken", "RefreshToken", "OAuthToken", "Authorization", "ClientSecret", "Email" };
        return prohibited.All(value => !json.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static void Validate(AiStudyConversation conversation)
    {
        if (conversation.Messages.Any(message => string.IsNullOrWhiteSpace(message.Role) || string.IsNullOrWhiteSpace(message.Content)))
            throw new InvalidOperationException("AI conversation messages must have a role and content.");
        var serialized = JsonSerializer.Serialize(conversation);
        if (ResearchPackageExportService.ContainsSecretMaterial(serialized))
            throw new InvalidOperationException("AI conversation persistence rejected credential-like material.");
    }

    private static string PathFor(string studyId)
    {
        var safe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(studyId))).ToLowerInvariant();
        return Path.Combine(Folder, safe + ".json");
    }
}
