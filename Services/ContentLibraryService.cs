using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ContentLibraryService
{
    public static async Task<IReadOnlyList<ContentItem>> GetAsync(
        string researcherId,
        bool includeInactive = false)
    {
        await LegacyContentCompatibilityService
            .SynchronizeResearcherAsync(researcherId);
        return await ContentItemRepository
            .GetByResearcherAsync(researcherId, includeInactive);
    }

    public static async Task<ContentItem> CreateAsync(
        ContentItem item,
        EngagementObservation? observation = null)
    {
        Validate(item);
        var now = DateTime.UtcNow;
        item.Id = string.IsNullOrWhiteSpace(item.Id)
            ? Guid.NewGuid().ToString()
            : item.Id;
        item.CapturedAtUtc = item.CapturedAtUtc == default
            ? now
            : item.CapturedAtUtc;
        item.CreatedAtUtc = now;
        item.UpdatedAtUtc = now;
        await ContentItemRepository.CreateAsync(item);
        if (observation is not null)
        {
            observation.ContentItemId = item.Id;
            observation.CapturedAtUtc = item.CapturedAtUtc;
            await EngagementObservationRepository.CreateAsync(observation);
        }
        return item;
    }

    public static async Task UpdateAsync(ContentItem item)
    {
        Validate(item);
        await ContentItemRepository.UpdateAsync(item);
    }

    public static async Task<EngagementObservation> AddObservationAsync(
        string contentItemId,
        EngagementObservation observation)
    {
        if (await ContentItemRepository.GetByIdAsync(contentItemId) is null)
        {
            throw new InvalidOperationException("Content item was not found.");
        }

        observation.Id = Guid.NewGuid().ToString();
        observation.ContentItemId = contentItemId;
        observation.CapturedAtUtc = observation.CapturedAtUtc == default
            ? DateTime.UtcNow
            : observation.CapturedAtUtc;
        await EngagementObservationRepository.CreateAsync(observation);
        return observation;
    }

    private static void Validate(ContentItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ResearcherId))
        {
            throw new ArgumentException("Researcher ownership is required.");
        }
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            throw new ArgumentException("Content title is required.");
        }
        if (string.IsNullOrWhiteSpace(item.ContentType))
        {
            throw new ArgumentException("Content type is required.");
        }
    }
}
