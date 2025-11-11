namespace Lua;

static class EnumerableEx
{
    public static IEnumerable<IEnumerable<T>> GroupConsecutiveBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        if (keySelector == null)
            throw new ArgumentNullException(nameof(keySelector));

        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            yield break;
        }

        List<T> group = [enumerator.Current];
        var previousKey = keySelector(enumerator.Current);

        while (enumerator.MoveNext())
        {
            var currentKey = keySelector(enumerator.Current);

            if (!EqualityComparer<TKey>.Default.Equals(previousKey, currentKey))
            {
                yield return group;
                group = [];
            }

            group.Add(enumerator.Current);
            previousKey = currentKey;
        }

        yield return group;
    }
}