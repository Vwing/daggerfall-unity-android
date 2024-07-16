// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Vwing
// Contributors:    
// 
// Notes:
//

using System;
using System.Collections.Concurrent;

namespace DaggerfallWorkshop.Game.UserInterface
{
    public class ObjectPool<T>
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;

        public ObjectPool(Func<T> objectGenerator)
        {
            if (objectGenerator == null) throw new ArgumentNullException(nameof(objectGenerator));
            _objects = new ConcurrentBag<T>();
            _objectGenerator = objectGenerator;
        }

        public T Get() => _objects.TryTake(out T item) ? item : _objectGenerator();

        public void Return(T item) => _objects.Add(item);
    }
    public class UIWindowPool : ObjectPool<IUserInterfaceWindow>
    {
        public UIWindowPool(Func<IUserInterfaceWindow> objectGenerator) : base(objectGenerator)
        {
        }
    }
}