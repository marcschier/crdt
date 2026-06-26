// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// An operation-based (commutative) CRDT. Instead of exchanging state, replicas broadcast
/// the operations they apply locally; remote replicas replay them via <see cref="Apply"/>.
/// </summary>
/// <typeparam name="TOperation">
/// The operation type. Operations carry the causal metadata (a <see cref="Dot"/> and any
/// dependencies) required to make <see cref="Apply"/> safe under at-least-once,
/// causally-ordered delivery.
/// </typeparam>
/// <remarks>
/// <para>
/// Implementations make <see cref="Apply"/> <strong>idempotent</strong> (a duplicated
/// operation is a no-op) by tracking the operation's <see cref="Dot"/>, so consumers do
/// not need exactly-once delivery — at-least-once causal delivery is sufficient.
/// </para>
/// <para>
/// Operations are produced by the type's own mutating methods, which return the operation
/// to broadcast. Delivery ordering requirements (causal vs. none) are documented per type.
/// </para>
/// </remarks>
public interface IOperationConvergent<TOperation>
{
    /// <summary>Applies a (possibly remote, possibly duplicated) operation downstream.</summary>
    /// <param name="operation">The operation to apply.</param>
    /// <returns>
    /// <see langword="true"/> if the operation changed this replica's state;
    /// <see langword="false"/> if it was a duplicate or otherwise a no-op.
    /// </returns>
    bool Apply(TOperation operation);
}
