namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>A stored aggregate document with its optimistic concurrency version.</summary>
internal interface IVersionedDocument
{
    int Version { get; set; }
}
