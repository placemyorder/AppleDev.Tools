using System.Text.Json.Serialization;

namespace AppleAppStoreConnect;

public class IndividualResponse<TItem, TAttributes> : Response where TItem : Item<TAttributes>, new()
{
    public IndividualResponse() : base() { }

    [JsonPropertyName("data")]
    public TItem Data { get; set; } = new();
}