using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SteamKit2.Networking.Steam3;

public class SocketSelectList : IList
{
    private Socket?[] Values;

    private bool _removeFromStart;
    private int _removedCount;
    private int _valuesCount;

    public bool IsFixedSize => true;

    public bool IsReadOnly => false;

    public bool IsSynchronized => true;

    public object SyncRoot => Values;

    public SocketSelectList()
    {
        Values = Array.Empty<Socket>();
    }

    public void SetValues(ICollection<Socket> otherCollection)
    {
        if ( Values.Length > otherCollection.Count )
        {
            Array.Clear( Values, otherCollection.Count, Values.Length - otherCollection.Count );
        }
        else if (Values.Length < otherCollection.Count)
        {
            Values = new Socket[ otherCollection.Count ];
        }

        otherCollection.CopyTo( Values, 0 );

        _removedCount = 0;
        _removeFromStart = RuntimeInformation.IsOSPlatform( OSPlatform.Windows );
        _valuesCount = otherCollection.Count;
    }

    public IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }

    public void CopyTo(Array array, int index)
    {
        throw new NotImplementedException();
    }

    public Socket At(int index)
    {
        return Values[GetRealIndex(index)] ?? throw new NullReferenceException();
    }

    public int Count => _valuesCount - _removedCount;

    public int Add(object? value)
    {
        throw new NotImplementedException();
    }

    public void Clear()
    {
        _removedCount = _valuesCount;
    }

    public bool Contains(object? value)
    {
        throw new NotImplementedException();
    }

    public int IndexOf(object? value)
    {
        throw new NotImplementedException();
    }

    public void Insert(int index, object? value)
    {
        throw new NotImplementedException();
    }

    public void Remove(object? value)
    {
        throw new NotImplementedException();
    }

    public void RemoveAt(int index)
    {
        var realIndexToRemove = GetRealIndex(index);
        var realIndexToReplace = _removeFromStart ? _removedCount : Count - 1;

        Values[ realIndexToRemove ] = Values[ realIndexToReplace ];
        Values[ realIndexToReplace ] = null;

        _removedCount++;
    }

    public object? this[int index]
    {
        get => Values[GetRealIndex(index)];
        set => throw new NotImplementedException();
    }

    private int GetRealIndex(int index)
    {
        return _removeFromStart ? _removedCount + index : index;
    }
}
