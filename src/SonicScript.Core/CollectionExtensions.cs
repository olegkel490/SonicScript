using System.Collections.ObjectModel;

namespace SonicScript.Core;

public static class CollectionExtensions
{
    /// <summary><c>List.RemoveAll</c> für <see cref="Collection{T}"/> (z. B. ObservableCollection).</summary>
    public static int RemoveAll<T>(this Collection<T> collection, Predicate<T> match)
    {
        var removed = 0;
        for (var i = collection.Count - 1; i >= 0; i--)
        {
            if (!match(collection[i])) continue;
            collection.RemoveAt(i);
            removed++;
        }
        return removed;
    }
}
