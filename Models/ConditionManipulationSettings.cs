using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SOCYVIA.Models;

public enum MetricManipulationMode
{
    Original,
    Hidden,
    Fixed,
    Multiplier,
    RandomRange
}


public enum ContentOrderMode
{
    Original,
    Chronological,
    ReverseChronological,
    Random,
    Popularity,
    Custom
}


public class ConditionManipulationSettings
{
    public bool ShowEngagementMetrics { get; set; } = true;

    public MetricManipulationMode LikesMode { get; set; } =
        MetricManipulationMode.Original;

    public long? LikesFixedValue { get; set; }
    public double? LikesMultiplier { get; set; }
    public long? LikesRandomMin { get; set; }
    public long? LikesRandomMax { get; set; }

    public MetricManipulationMode CommentsMode { get; set; } =
        MetricManipulationMode.Original;

    public long? CommentsFixedValue { get; set; }
    public double? CommentsMultiplier { get; set; }
    public long? CommentsRandomMin { get; set; }
    public long? CommentsRandomMax { get; set; }

    public MetricManipulationMode SharesMode { get; set; } =
        MetricManipulationMode.Original;

    public long? SharesFixedValue { get; set; }
    public double? SharesMultiplier { get; set; }
    public long? SharesRandomMin { get; set; }
    public long? SharesRandomMax { get; set; }

    public MetricManipulationMode SavesMode { get; set; } =
        MetricManipulationMode.Original;

    public long? SavesFixedValue { get; set; }
    public double? SavesMultiplier { get; set; }
    public long? SavesRandomMin { get; set; }
    public long? SavesRandomMax { get; set; }

    public MetricManipulationMode ViewsMode { get; set; } =
        MetricManipulationMode.Original;

    public long? ViewsFixedValue { get; set; }
    public double? ViewsMultiplier { get; set; }
    public long? ViewsRandomMin { get; set; }
    public long? ViewsRandomMax { get; set; }

    public ContentOrderMode ContentOrderMode { get; set; } =
        ContentOrderMode.Original;

    public bool ShowAuthor { get; set; } = true;
    public bool ShowTimestamp { get; set; } = true;
    public bool ShowPlatformIdentity { get; set; } = true;

    public string? CustomPresentationJson { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
