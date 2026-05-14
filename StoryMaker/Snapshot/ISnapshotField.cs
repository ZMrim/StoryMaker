namespace StoryMaker.Snapshot;

public interface ISnapshotField
{
    string Key { get; }
    object Collect();
    bool IncludeInLowTokenMode { get; }
}
