using LinqToDB.Data;
using LinqToDB;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Identity.Client;

namespace DomainCore.Repository;

public enum OperationType
{
    Insert,
    Update,
    Delete
}

public class ChangeSet
{
    public IList<object> Inserts { get; set; }
    public IList<object> Updates { get; set; }
    public IList<object> Deletes { get; set; }
}

public class TxObjectStore
{
    private Dictionary<OperationType, Dictionary<Type, List<object>>> _store;

    public TxObjectStore()
    {
        Initialize();
    }

    public void Add<T>(T item, OperationType operation)
    {
        var type = typeof(T);
        if (!_store[operation].ContainsKey(type))
        {
            _store[operation][type] = new List<object>();
        }
        _store[operation][type].Add(item);
    }

    public void SubmitAll(DataConnection connection)
    {
        ProcessAll(connection, OperationType.Insert, "InsertWithInt32Identity");
        ProcessAll(connection, OperationType.Update, "Update");
        ProcessAll(connection, OperationType.Delete, "Delete");
    }

    public ChangeSet GetChangeSet()
    {
        return new ChangeSet
        {
            Inserts = _store[OperationType.Insert].Values.SelectMany(list => list).ToList(),
            Updates = _store[OperationType.Update].Values.SelectMany(list => list).ToList(),
            Deletes = _store[OperationType.Delete].Values.SelectMany(list => list).ToList()
        };
    }

    public void Clear()
    {
        Initialize();
    }

    private void Initialize()
    {
        _store = new()
        {
            [OperationType.Insert] = new Dictionary<Type, List<object>>(),
            [OperationType.Update] = new Dictionary<Type, List<object>>(),
            [OperationType.Delete] = new Dictionary<Type, List<object>>()
        };
    }

    private void ProcessAll(DataConnection connection, OperationType operation, string methodName)
    {
        var methodInfo = typeof(DataExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == methodName && m.IsGenericMethod);

        if (methodInfo == null)
            throw new InvalidOperationException($"{methodName}<T> method not found.");

        foreach (var typeAndObjects in _store[operation])
        {
            var type = typeAndObjects.Key;
            var objects = typeAndObjects.Value;

            var genericMethod = methodInfo.MakeGenericMethod(type);
            PropertyInfo? pkProperty = null;
            bool isInsertWithIdentity = methodName.StartsWith("InsertWith");

            if (isInsertWithIdentity)
            {
                // Find primary key property marked as [Column(IsPrimaryKey = true, IsIdentity = true)]
                pkProperty = type.GetProperties()
                    .FirstOrDefault(p => p.GetCustomAttributes(typeof(LinqToDB.Mapping.ColumnAttribute), true)
                        .Cast<LinqToDB.Mapping.ColumnAttribute>()
                        .Any(a => a.IsPrimaryKey && a.IsIdentity));
            }

            foreach (var obj in objects)
            {
                object? result = null;

                if (typeof(IHasStoredProcOperations).IsAssignableFrom(type))
                {
                    var spEntity = (IHasStoredProcOperations)obj;
                    var handled = false;

                    switch (operation)
                    {
                        case OperationType.Insert:
                            handled = spEntity.ExecuteInsert(connection);
                            break;
                        case OperationType.Update:
                            handled = spEntity.ExecuteUpdate(connection);
                            break;
                        case OperationType.Delete:
                            handled = spEntity.ExecuteDelete(connection);
                            break;
                    }

                    if (handled)
                        continue;
                }

                result = genericMethod.Invoke(null, new object[]
                {
                    connection, // dataContext
                    obj,        // obj
                    null,       // tableName (default)
                    null,       // databaseName (default)
                    null,       // schemaName (default)
                    null,       // serverName (default)
                    TableOptions.None // tableOptions (default)
                });

                if (isInsertWithIdentity && pkProperty != null && result != null)
                {
                    pkProperty.SetValue(obj, result);
                }
            }
        }
    }
}

public partial class MyDataConnection : DataConnection
{
    private TxObjectStore txObjectStore { get; } = new TxObjectStore();

    public void InsertOnSubmit<T>(T entity)
    {
        txObjectStore.Add(entity, OperationType.Insert);
    }

    public void InsertAssociationOnSubmit<TChild, TParent>(TChild child, TParent parent)
        where TChild : class
        where TParent : class
    {
        txObjectStore.Add(child, OperationType.Insert);

        // Use reflection to find a property of type IEnumerable<TChild>
        var childrenProperty = typeof(TParent).GetProperties()
            .FirstOrDefault(prop => typeof(IEnumerable<TChild>).IsAssignableFrom(prop.PropertyType));

        if (childrenProperty == null)
        {
            throw new InvalidOperationException($"No property of type IEnumerable<{typeof(TChild).Name}> found on {typeof(TParent).Name}");
        }

        // Get the collection
        var children = childrenProperty.GetValue(parent) as ICollection<TChild>;

        if (children == null)
        {
            children = new List<TChild>();

            // Set the newly created collection to the property
            childrenProperty.SetValue(parent, children);
        }

        children.Add(child);

    }

    public void UpdateOnSubmit<T>(T entity)
    {
        txObjectStore.Add(entity, OperationType.Update);
    }

    public void DeleteOnSubmit<T>(T entity)
    {
        txObjectStore.Add(entity, OperationType.Delete);
    }

    public void DeleteAllOnSubmit<T>(IEnumerable<T> entities)
    {
        foreach (var entity in entities) txObjectStore.Add(entity, OperationType.Delete);
    }

    public void RemoveAssociationOnSubmit<TChild, TParent>(TChild child, TParent parent)
        where TChild : class
        where TParent : class
    {
        txObjectStore.Add(child, OperationType.Delete);

        // Use reflection to find a property of type IEnumerable<TChild>
        var childrenProperty = typeof(TParent).GetProperties()
            .FirstOrDefault(prop => typeof(IEnumerable<TChild>).IsAssignableFrom(prop.PropertyType));

        if (childrenProperty == null)
        {
            throw new InvalidOperationException($"No property of type IEnumerable<{typeof(TChild).Name}> found on {typeof(TParent).Name}");
        }

        // Get the collection
        var children = childrenProperty.GetValue(parent) as ICollection<TChild>;

        if (children != null && children.Count != 0)
        {
            children.Remove(child);
        }
    }

    public void SubmitChanges()
    {
        using (var transaction = BeginTransaction())
        {
            try
            {
                // Execute operations
                txObjectStore.SubmitAll(this);

                // Clear tracked changes
                txObjectStore.Clear();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public ChangeSet GetChangeSet()
    {
        return txObjectStore.GetChangeSet();
    }
}