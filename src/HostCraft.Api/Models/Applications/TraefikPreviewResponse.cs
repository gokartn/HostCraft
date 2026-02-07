using System.Collections.Generic;

namespace HostCraft.Api.Models.Applications;

public record TraefikPreviewResponse(
    Dictionary<string, string> BaseLabels,
    Dictionary<string, string> Overrides,
    Dictionary<string, string> MergedLabels,
    List<string> Warnings);
