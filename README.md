# Linq2DbHelper

The **Linq2DbHelper** project is to enhance Linq2Db to act more like Linq2Sql. It provides a helper `MyDataConnection` partial class that extends `Linq2DB.Data.DataConnection`. 
It is designed to seamlessly integrate with your own `DataConnection` class and enhance its functionality through reflection.

## Features

1. **Transactional Operations**  
   Supports transactional semantics for `Insert`, `Update`, and `Delete` operations, allowing them to run within the same transaction.  

2. **Primary Key Auto-Population**  
   Automatically assigns the primary key value back to the entity object after an `Insert`.  

3. **Association Management**  
   Handles insertion and removal of associated objects when entities reference other entities via associations.  

4. **Stored Procedure Support**  
   Automatically uses stored procedures for operations if they are defined, instead of the built-in Linq2DB methods.  

---

## Usage

### 1. Integrating with Your DataConnection

If your custom `DataConnection` class is named `MyActualDataConnection`, rename the provided partial class to `MyActualDataConnection` as well, and compile them together.  

Use the helper methods just as you would in Linq2SQL:

```csharp
using (var db = new MyActualDataConnection())
{
    var customer = new Customer { Name = "Alice" };

    db.InsertOnSubmit(customer);  // Queue insert
    db.UpdateOnSubmit(customer);  // Queue update
    db.DeleteOnSubmit(customer);  // Queue delete

    db.SubmitChanges(); // Commit all operations in a single transaction
}
```

### 2. Performing operations

InsertOnSubmit, UpdateOnSubmit, and DeleteOnSubmit queue changes.

SubmitChanges() commits them in one transaction.

Primary keys are auto-populated after insertion.

### 3. Overriding with Stored Procedures

To override the default **Insert**, **Update**, and **Delete** operations with stored procedures:

1. Create a class that implements `IHasStoredProcOperations`.
2. Define the stored procedures in your database.
3. `Linq2DbHelper` will automatically detect and call them using reflection.

## Attribution

This project was created by and is the property of **Fraser River Software Inc**.  
If you use or modify this project, please provide clear attribution to the original author.  

🌐 [Fraser River Software Inc](https://fraserriver.ai)
