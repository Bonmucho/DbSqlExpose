namespace DbSqlExpose;

/// <summary>
/// Represents the different modes for SQL query results.
/// </summary>
public enum QueryResultMode
{
	/// <summary>
	/// No query reader result expected. Returns the number of affected rows.
	/// </summary>
	None,
	
	/// <summary>
	/// Single value result. If the query returns multiple values, only the first one is returned.
	/// </summary>
	Scalar,
	
	/// <summary>
	/// Multiple rows result. This is the default behavior.
	/// </summary>
	Enumeration,
	
	/// <summary>
	/// Multiple result sets. If a script contains multiple queries, each of which returns
	/// results on it's own, all of these sets are returned.
	/// </summary>
	MultiSet
}