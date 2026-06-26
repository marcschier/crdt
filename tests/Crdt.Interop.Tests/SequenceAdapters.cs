// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Interop.Tests;

internal interface ISequenceAdapter<TSelf>
{
    void InsertText(ReplicaId replicaId, int index, string text);

    void ApplyOperation(ReplicaId replicaId, CrdtOperation operation);

    void MergeSequence(TSelf other);

    TSelf CloneSequence();

    string Render();
}

internal static class SequenceAdapterOperations
{
    public static void ApplyInsertText<TSequence>(TSequence sequence, ReplicaId replicaId, int index, string text)
        where TSequence : ICharSequence<TSequence>
    {
        for (int i = 0; i < text.Length; i++)
        {
            sequence.Insert(replicaId, index + i, text[i]);
        }
    }

    public static void ApplyDeleteText<TSequence>(TSequence sequence, int index, int length)
        where TSequence : ICharSequence<TSequence>
    {
        for (int i = 0; i < length; i++)
        {
            sequence.Delete(index);
        }
    }

    public static void ApplyOperation<TSequence>(TSequence sequence, ReplicaId replicaId, CrdtOperation operation)
        where TSequence : ICharSequence<TSequence>
    {
        if (operation.Op == "insert")
        {
            ApplyInsertText(sequence, replicaId, operation.Index, operation.Text ?? string.Empty);
            return;
        }

        if (operation.Op == "delete")
        {
            ApplyDeleteText(sequence, operation.Index, operation.Length);
            return;
        }

        throw new InvalidOperationException($"Unsupported operation '{operation.Op}'.");
    }
}

internal interface ICharSequence<TSelf>
{
    void Insert(ReplicaId replicaId, int index, char value);

    void Delete(int index);
}

internal static class SequenceRendering
{
    public static string Render(char[] values) => new(values);
}

internal sealed class RgaAdapter : ISequenceAdapter<RgaAdapter>, ICharSequence<RgaAdapter>
{
    private readonly Rga<char> _sequence;

    public RgaAdapter()
        : this(new Rga<char>())
    {
    }

    private RgaAdapter(Rga<char> sequence) => _sequence = sequence;

    public void Insert(ReplicaId replicaId, int index, char value) => _sequence.Insert(replicaId, index, value);

    public void Delete(int index) => _sequence.Delete(index);

    public void InsertText(ReplicaId replicaId, int index, string text) =>
        SequenceAdapterOperations.ApplyInsertText(this, replicaId, index, text);

    public void ApplyOperation(ReplicaId replicaId, CrdtOperation operation) =>
        SequenceAdapterOperations.ApplyOperation(this, replicaId, operation);

    public void MergeSequence(RgaAdapter other) => _sequence.Merge(other._sequence);

    public RgaAdapter CloneSequence() => new(_sequence.Clone());

    public string Render() => SequenceRendering.Render(_sequence.ToArray());
}

internal sealed class LogootAdapter : ISequenceAdapter<LogootAdapter>, ICharSequence<LogootAdapter>
{
    private readonly LogootSequence<char> _sequence;

    public LogootAdapter()
        : this(new LogootSequence<char>())
    {
    }

    private LogootAdapter(LogootSequence<char> sequence) => _sequence = sequence;

    public void Insert(ReplicaId replicaId, int index, char value) => _sequence.Insert(replicaId, index, value);

    public void Delete(int index) => _sequence.Delete(index);

    public void InsertText(ReplicaId replicaId, int index, string text) =>
        SequenceAdapterOperations.ApplyInsertText(this, replicaId, index, text);

    public void ApplyOperation(ReplicaId replicaId, CrdtOperation operation) =>
        SequenceAdapterOperations.ApplyOperation(this, replicaId, operation);

    public void MergeSequence(LogootAdapter other) => _sequence.Merge(other._sequence);

    public LogootAdapter CloneSequence() => new(_sequence.Clone());

    public string Render() => SequenceRendering.Render(_sequence.ToArray());
}

internal sealed class LSeqAdapter : ISequenceAdapter<LSeqAdapter>, ICharSequence<LSeqAdapter>
{
    private readonly LSeqSequence<char> _sequence;

    public LSeqAdapter()
        : this(new LSeqSequence<char>())
    {
    }

    private LSeqAdapter(LSeqSequence<char> sequence) => _sequence = sequence;

    public void Insert(ReplicaId replicaId, int index, char value) => _sequence.Insert(replicaId, index, value);

    public void Delete(int index) => _sequence.Delete(index);

    public void InsertText(ReplicaId replicaId, int index, string text) =>
        SequenceAdapterOperations.ApplyInsertText(this, replicaId, index, text);

    public void ApplyOperation(ReplicaId replicaId, CrdtOperation operation) =>
        SequenceAdapterOperations.ApplyOperation(this, replicaId, operation);

    public void MergeSequence(LSeqAdapter other) => _sequence.Merge(other._sequence);

    public LSeqAdapter CloneSequence() => new(_sequence.Clone());

    public string Render() => SequenceRendering.Render(_sequence.ToArray());
}

internal sealed class TreedocAdapter : ISequenceAdapter<TreedocAdapter>, ICharSequence<TreedocAdapter>
{
    private readonly TreedocSequence<char> _sequence;

    public TreedocAdapter()
        : this(new TreedocSequence<char>())
    {
    }

    private TreedocAdapter(TreedocSequence<char> sequence) => _sequence = sequence;

    public void Insert(ReplicaId replicaId, int index, char value) => _sequence.Insert(replicaId, index, value);

    public void Delete(int index) => _sequence.Delete(index);

    public void InsertText(ReplicaId replicaId, int index, string text) =>
        SequenceAdapterOperations.ApplyInsertText(this, replicaId, index, text);

    public void ApplyOperation(ReplicaId replicaId, CrdtOperation operation) =>
        SequenceAdapterOperations.ApplyOperation(this, replicaId, operation);

    public void MergeSequence(TreedocAdapter other) => _sequence.Merge(other._sequence);

    public TreedocAdapter CloneSequence() => new(_sequence.Clone());

    public string Render() => SequenceRendering.Render(_sequence.ToArray());
}

internal sealed class YataAdapter : ISequenceAdapter<YataAdapter>, ICharSequence<YataAdapter>
{
    private readonly YataSequence<char> _sequence;

    public YataAdapter()
        : this(new YataSequence<char>())
    {
    }

    private YataAdapter(YataSequence<char> sequence) => _sequence = sequence;

    public void Insert(ReplicaId replicaId, int index, char value) => _sequence.Insert(replicaId, index, value);

    public void Delete(int index) => _sequence.Delete(index);

    public void InsertText(ReplicaId replicaId, int index, string text) =>
        SequenceAdapterOperations.ApplyInsertText(this, replicaId, index, text);

    public void ApplyOperation(ReplicaId replicaId, CrdtOperation operation) =>
        SequenceAdapterOperations.ApplyOperation(this, replicaId, operation);

    public void MergeSequence(YataAdapter other) => _sequence.Merge(other._sequence);

    public YataAdapter CloneSequence() => new(_sequence.Clone());

    public string Render() => SequenceRendering.Render(_sequence.ToArray());
}

internal sealed class WootAdapter : ISequenceAdapter<WootAdapter>, ICharSequence<WootAdapter>
{
    private readonly WootSequence<char> _sequence;

    public WootAdapter()
        : this(new WootSequence<char>())
    {
    }

    private WootAdapter(WootSequence<char> sequence) => _sequence = sequence;

    public void Insert(ReplicaId replicaId, int index, char value) => _sequence.Insert(replicaId, index, value);

    public void Delete(int index) => _sequence.Delete(index);

    public void InsertText(ReplicaId replicaId, int index, string text) =>
        SequenceAdapterOperations.ApplyInsertText(this, replicaId, index, text);

    public void ApplyOperation(ReplicaId replicaId, CrdtOperation operation) =>
        SequenceAdapterOperations.ApplyOperation(this, replicaId, operation);

    public void MergeSequence(WootAdapter other) => _sequence.Merge(other._sequence);

    public WootAdapter CloneSequence() => new(_sequence.Clone());

    public string Render() => SequenceRendering.Render(_sequence.ToArray());
}
