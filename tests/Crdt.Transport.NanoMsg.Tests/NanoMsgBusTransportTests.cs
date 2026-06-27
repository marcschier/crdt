// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport.NanoMsg.Tests;

public sealed class NanoMsgBusTransportTests
{
    private static NanoMsgBusTransportOptions ValidOptions() =>
        new() { BindAddress = "tcp://127.0.0.1:0" };

    [Test]
    public async Task Constructor_Rejects_Null_Options()
    {
        await Assert.That(() => new NanoMsgBusTransport(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_Rejects_No_Bind_Or_Peers()
    {
        await Assert.That(() => new NanoMsgBusTransport(new NanoMsgBusTransportOptions()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_Rejects_NonPositive_MaxFrameLength()
    {
        await Assert.That(() => new NanoMsgBusTransport(
            new NanoMsgBusTransportOptions { BindAddress = "tcp://127.0.0.1:0", MaxFrameLength = 0 }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_Accepts_Peers_Without_Bind()
    {
        var options = new NanoMsgBusTransportOptions();
        options.Peers.Add("tcp://127.0.0.1:5560");
        var transport = new NanoMsgBusTransport(options);

        await using (transport)
        {
            await Assert.That(transport.BoundPort).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task AddPeer_Rejects_Empty_Address()
    {
        var transport = new NanoMsgBusTransport(ValidOptions());

        await using (transport)
        {
            await Assert.That(() => transport.AddPeer("  ")).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task AddPeers_Rejects_Null()
    {
        var transport = new NanoMsgBusTransport(ValidOptions());

        await using (transport)
        {
            await Assert.That(() => transport.AddPeers(null!)).Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task SendAsync_Rejects_Oversize_Frame_Without_Network()
    {
        var transport = new NanoMsgBusTransport(
            new NanoMsgBusTransportOptions { BindAddress = "tcp://127.0.0.1:0", MaxFrameLength = 8 });

        await using (transport)
        {
            byte[] big = FrameCodec.Encode(MessageType.State, new byte[64]);
            bool rejected = false;
            try
            {
                await transport.SendAsync(big);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            await Assert.That(rejected).IsTrue();
        }
    }

    [Test]
    public async Task SendAsync_Throws_When_Not_Started()
    {
        var transport = new NanoMsgBusTransport(ValidOptions());

        await using (transport)
        {
            byte[] frame = FrameCodec.Encode(MessageType.State, [1, 2, 3]);
            bool threw = false;
            try
            {
                await transport.SendAsync(frame);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            await Assert.That(threw).IsTrue();
        }
    }

    [Test]
    public async Task DisposeAsync_Before_Start_Is_Safe_And_Idempotent()
    {
        var transport = new NanoMsgBusTransport(ValidOptions());

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        await Assert.That(transport.BoundPort).IsEqualTo(-1);
    }
}
