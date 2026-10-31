namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Deep-merges a user supplied JSON object into a generated configuration object.
/// Objects are merged recursively, everything else (arrays, scalars, null) replaces the target value.
/// </summary>
public static class UnifiedJsonMerge
{
    public static T? Merge<T>(T target, string? patchJson) where T : class
    {
        if (patchJson.IsNullOrEmpty())
        {
            return target;
        }
        if (JsonUtils.ParseJson(patchJson) is not JsonObject patch
            || JsonUtils.SerializeToNode(target) is not JsonObject node)
        {
            Logging.SaveLog($"{nameof(UnifiedJsonMerge)}: invalid JSON object: {patchJson}");
            return target;
        }
        MergeInto(node, patch);
        return JsonUtils.Deserialize<T>(node.ToJsonString()) ?? target;
    }

    public static void MergeInto(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (value is JsonObject patchObj && target[key] is JsonObject targetObj)
            {
                MergeInto(targetObj, patchObj);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }

    public static bool IsValidObject(string? json) => json.IsNullOrEmpty() || JsonUtils.ParseJson(json) is JsonObject;
}
