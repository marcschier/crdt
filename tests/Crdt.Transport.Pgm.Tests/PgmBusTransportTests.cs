// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Net;

namespace Crdt.Transport.Pgm.Tests;

public sealed class PgmBusTransportTests
{
    private static PgmBusTransportOptions ValidOptions() =>
        new() { InMemoryBus = new InMemoryMulticastBus() };

    [Test]
    public async Task Constructor_Rejects_Null_Options()
    {
        await Assert.That(() => new PgmBusTransport(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_Rejects_Null_Multicast_Group()
    {
        await Assert.That(() => new PgmBusTransport(new PgmBusTransportOptions { MulticastGroup = null! }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_Rejects_NonPositive_MaxFrameLength()
    {
        await Assert.That(() => new PgmBusTransport(new PgmBusTransportOptions { MaxFrameLength = 0 }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StartAsync_And_DisposeAsync_Are_Idempotent()
    {
        var transport = new PgmBusTransport(ValidOptions());

        await transport.StartAsync();
        await transport.StartAsync();
        await transport.DisposeAsync();
        await transport.DisposeAsync();
    }

    [Test]
    public async Task SendAsync_Rejects_Oversize_Frame_Without_Network()
    {
        var transport = new PgmBusTransport(new PgmBusTransportOptions
        {
            InMemoryBus = new InMemoryMulticastBus(),
            MaxFrameLength = 8,
        });

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
        var transport = new PgmBusTransport(ValidOptions());

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
        var transport = new PgmBusTransport(ValidOptions());

        await transport.DisposeAsync();
        await transport.DisposeAsync();
    }
}
