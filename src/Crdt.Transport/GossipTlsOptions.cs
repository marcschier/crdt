// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Crdt.Transport;

/// <summary>Configures optional TLS for a <see cref="TcpGossipTransport"/>.</summary>
/// <remarks>
/// A gossip node acts as both server (accepting peer connections) and client (connecting to peers),
/// so these options carry both roles. The <see cref="RemoteCertificateValidationCallback"/> validates
/// the server certificate when connecting and the client certificate when accepting with
/// <see cref="RequireClientCertificate"/> set.
/// </remarks>
public sealed class GossipTlsOptions
{
    /// <summary>Gets or sets the certificate presented when accepting inbound connections.</summary>
    public X509Certificate2? ServerCertificate { get; set; }

    /// <summary>Gets or sets a value indicating whether inbound peers must present a client certificate.</summary>
    public bool RequireClientCertificate { get; set; }

    /// <summary>Gets or sets the target host used for SNI and validation when connecting.</summary>
    /// <remarks>When <see langword="null"/>, the peer's IP address is used.</remarks>
    public string? TargetHost { get; set; }

    /// <summary>Gets or sets the certificates offered when connecting (mutual TLS).</summary>
    public X509CertificateCollection? ClientCertificates { get; set; }

    /// <summary>Gets or sets the callback that validates the remote certificate.</summary>
    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

    /// <summary>Gets or sets the allowed TLS protocol versions.</summary>
    /// <remarks><see cref="SslProtocols.None"/> (the default) lets the operating system negotiate.</remarks>
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;

    /// <summary>Gets or sets a value indicating whether the certificate revocation list is checked.</summary>
    public bool CheckCertificateRevocation { get; set; }
}
