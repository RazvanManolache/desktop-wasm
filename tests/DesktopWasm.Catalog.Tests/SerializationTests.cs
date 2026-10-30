namespace DesktopWasm.Catalog.Tests;

public sealed class SerializationTests
{
    [Fact]
    public void SerializeIsDeterministic()
    {
        var apps = new[]
        {
            Fx.Descriptor("fixture-b", aliases: ["fixture-b-legacy", "/app/fixture-b"]),
            Fx.Descriptor("fixture-a"),
        };
        var first = CatalogSerializer.Serialize(apps);
        var second = CatalogSerializer.Serialize(apps.AsEnumerable().Reverse());
        Assert.Equal(first, second);
        Assert.StartsWith("\"schemaVersion\": 2", first.TrimStart('{', ' ', '\n', '\r'));
    }

    [Fact]
    public void CapabilitiesSerializeAsArray()
    {
        var json = CatalogSerializer.Serialize([Fx.Descriptor("fixture-m",
            capabilities: DesktopWasm.AppModel.AppCapability.Mixed)]);
        var normalized = json.Replace("\r\n", "\n");
        Assert.Contains("\"capabilities\": [\n        \"winforms\",\n        \"wpf\"\n      ]", normalized);
        var registry = CatalogSerializer.Deserialize(json);
        Assert.Equal(DesktopWasm.AppModel.AppCapability.Mixed,
            registry.Resolve("fixture-m").Capabilities);
    }

    [Fact]
    public void RoundtripValidatesDescriptors()
    {
        var json = CatalogSerializer.Serialize([Fx.Descriptor("fixture-ok")]);
        var registry = CatalogSerializer.Deserialize(json);
        Assert.Equal("fixture-ok", registry.Resolve("fixture-ok").Id);
    }

    [Fact]
    public void BadSchemaVersionRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            CatalogSerializer.Deserialize("{\"schemaVersion\": 99, \"apps\": []}"));
    }

    [Fact]
    public void InvalidEntryRejectedOnDeserialize()
    {
        var json = CatalogSerializer.Serialize([Fx.Available("fixture-ok")]);
        var tampered = json.Replace("fixture-ok", "../escape");
        Assert.Throws<InvalidDataException>(() => CatalogSerializer.Deserialize(tampered));
    }
}
