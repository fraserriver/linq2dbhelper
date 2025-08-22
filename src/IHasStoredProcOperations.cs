using LinqToDB.Data;

public interface IHasStoredProcOperations
{
    bool ExecuteInsert(DataConnection connection);
    bool ExecuteUpdate(DataConnection connection);
    bool ExecuteDelete(DataConnection connection);
}
