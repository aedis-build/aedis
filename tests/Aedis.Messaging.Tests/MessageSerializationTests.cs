using System.Text;
using Aedis.Messaging.Abstractions.Serialization;
using FluentAssertions;
using MessagePack;
using NSubstitute;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary>
///     A strategy de serialização isolada: round-trip de cada formato e a resolução por dado
///     (publish) e por content-type (consume).
/// </summary>
public sealed class MessageSerializationTests
{
    public sealed class PlainPoco
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public sealed class PackedPoco
    {
        [Key(0)] public int Id { get; set; }
        [Key(1)] public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void Json_faz_roundtrip() {
        var sut = new JsonMessageSerializer();
        var original = new PlainPoco { Id = 7, Name = "aedis" };

        var bytes = sut.Serialize(original);
        var back = (PlainPoco?)sut.Deserialize(bytes, typeof(PlainPoco));

        sut.ContentType.Should().Be("application/json");
        back.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void MessagePack_faz_roundtrip() {
        var sut = new MessagePackMessageSerializer();
        var original = new PackedPoco { Id = 9, Name = "msgpack" };

        var bytes = sut.Serialize(original);
        var back = (PackedPoco?)sut.Deserialize(bytes, typeof(PackedPoco));

        sut.ContentType.Should().Be("application/x-msgpack");
        back.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void PlainText_faz_roundtrip() {
        var sut = new PlainTextMessageSerializer();

        var bytes = sut.Serialize("olá mundo");
        var back = sut.Deserialize(bytes, typeof(string));

        sut.ContentType.Should().Be("text/plain");
        back.Should().Be("olá mundo");
    }

    [Fact]
    public void RawBytes_e_passthrough() {
        var sut = new RawBytesMessageSerializer();
        var payload = Encoding.UTF8.GetBytes("binário");

        var bytes = sut.Serialize(payload);
        var back = sut.Deserialize(bytes, typeof(byte[]));

        sut.ContentType.Should().Be("application/octet-stream");
        ((byte[])back!).Should().Equal(payload);
    }

    [Theory]
    [InlineData(typeof(byte[]), typeof(RawBytesMessageSerializer))]
    [InlineData(typeof(string), typeof(PlainTextMessageSerializer))]
    [InlineData(typeof(PackedPoco), typeof(MessagePackMessageSerializer))]
    [InlineData(typeof(PlainPoco), typeof(JsonMessageSerializer))]
    public void Resolver_escolhe_a_estrategia_por_dado(Type dataType, Type expectedSerializer) {
        object data = dataType == typeof(byte[]) ? new byte[] { 1, 2 }
            : dataType == typeof(string) ? "x"
            : Activator.CreateInstance(dataType)!;

        var resolved = MessageSerializerResolver.CreateDefault().ResolveForSerialize(data);

        resolved.Should().BeOfType(expectedSerializer);
    }

    [Theory]
    [InlineData("application/json", typeof(JsonMessageSerializer))]
    [InlineData("application/x-msgpack", typeof(MessagePackMessageSerializer))]
    [InlineData("text/plain", typeof(PlainTextMessageSerializer))]
    [InlineData("application/octet-stream", typeof(RawBytesMessageSerializer))]
    [InlineData("desconhecido/xyz", typeof(JsonMessageSerializer))]
    public void Resolver_escolhe_a_estrategia_por_content_type(string contentType, Type expectedSerializer) {
        var resolved = MessageSerializerResolver.CreateDefault().ResolveForContentType(contentType);

        resolved.Should().BeOfType(expectedSerializer);
    }

    [Fact]
    public void Resolver_usa_a_primeira_estrategia_que_aceita_o_dado() {
        var aceita = Substitute.For<IMessageSerializer>();
        aceita.CanSerialize(Arg.Any<object>()).Returns(true);
        var ignora = Substitute.For<IMessageSerializer>();
        ignora.CanSerialize(Arg.Any<object>()).Returns(false);

        var resolver = new MessageSerializerResolver([ignora, aceita, new JsonMessageSerializer()]);

        resolver.ResolveForSerialize(new object()).Should().BeSameAs(aceita);
    }
}
