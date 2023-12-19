using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DbSqlExpose;

/// <summary>
/// Provides extension methods for a web application to execute SQL database queries.
/// </summary>
public static class WebApplicationSqlExtensions
{
	/// <summary>
	/// Exposes the database context for handling SQL queries via HTTP methods.
	/// 
	/// <b>Caution</b>: Exposing databases via HTTP can pose security risks, such as SQL injection attacks.
	/// Therefore, consider strong and extensive access restrictions
	/// (e.g., network host binding, IP address filtering, or authentication mechanisms).
	/// </summary>
	/// <typeparam name="T">The type of DbContext.</typeparam>
	/// <param name="app">The WebApplication instance.</param>
	/// <param name="pattern">The route pattern for the SQL query endpoint. Default is "/Query".</param>
	/// <param name="methods">The HTTP methods to be supported. Default includes GET, PUT, POST, DELETE.</param>
	/// <param name="jsonOptions">Options for JSON serialization.</param>
	/// <returns>A RouteHandlerBuilder for further configurations.</returns>
	public static RouteHandlerBuilder ExposeDbContext<T>(this WebApplication app, string pattern = "/Query",
		IEnumerable<HttpMethod>? methods = null, JsonWriterOptions jsonOptions = default) where T : DbContext =>
		app.MapMethods(pattern, (methods ?? [HttpMethod.Get, HttpMethod.Put, HttpMethod.Post, HttpMethod.Delete])
			.Select(x => x.ToString()),
			async (HttpContext hc, T db, [FromQuery] string sql,
				[FromQuery] QueryResultMode mode = QueryResultMode.Enumeration,
				[FromQuery] IsolationLevel isolationLevel = IsolationLevel.Unspecified,
				CancellationToken cancellationToken = default) =>
			{
				hc.Response.ContentType = MediaTypeNames.Application.Json;

				var connection = db.Database.GetDbConnection();
				
				await using var command = connection.CreateCommand();
				await using Utf8JsonWriter writer = new(hc.Response.BodyWriter, jsonOptions);
				await db.Database.OpenConnectionAsync(cancellationToken);
				await using var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken);
				
				command.CommandText = sql;
				command.Transaction = transaction;
				
				try
				{
					if (mode == QueryResultMode.None)
					{
						writer.WriteNumberValue(await command.ExecuteNonQueryAsync(cancellationToken));
						await transaction.CommitAsync(cancellationToken);
						return;
					}
				
					await using var reader = await command.ExecuteReaderAsync(cancellationToken);

					if (mode == QueryResultMode.MultiSet) writer.WriteStartArray();

					do
					{
						if (mode != QueryResultMode.Scalar) writer.WriteStartArray();
					
						var serialize = await CompileAsync(reader, writer, cancellationToken);

						if (serialize is not null)
						{
							WriteRecord(reader, writer, serialize);

							if (mode == QueryResultMode.Scalar)
							{
								await transaction.CommitAsync(cancellationToken);
								return;
							}
						
							while (await reader.ReadAsync(cancellationToken))
								WriteRecord(reader, writer, serialize);
						} else if (mode == QueryResultMode.Scalar) writer.WriteNullValue();
					
						if (mode != QueryResultMode.Scalar) writer.WriteEndArray();

						if (mode != QueryResultMode.MultiSet) break;
					} while (await reader.NextResultAsync(cancellationToken));

					if (mode == QueryResultMode.MultiSet) writer.WriteEndArray();
					
					await transaction.CommitAsync(cancellationToken);
				}
				catch (Exception e)
				{
					hc.Response.ContentType = MediaTypeNames.Text.Plain;
					hc.Response.StatusCode = (int)HttpStatusCode.BadRequest;
					await hc.Response.BodyWriter.WriteAsync(Encoding.UTF8.GetBytes(e.Message), cancellationToken);
				}
			});

	private static async Task<Action<DbDataReader, Utf8JsonWriter>?> CompileAsync(
		DbDataReader reader, Utf8JsonWriter writer, CancellationToken cancellationToken)
	{
		if (!await reader.ReadAsync(cancellationToken)) return null;
		
		var readerExpression = Expression.Parameter(typeof(DbDataReader), nameof(reader));
		var writerExpression = Expression.Parameter(typeof(Utf8JsonWriter), nameof(writer));

		List<Expression> expressions = [];
		
		for (var i = 0; i < reader.FieldCount; i++)
			switch (reader.GetValue(i))
			{
				case null:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteNull), null,
						Expression.Constant(reader.GetName(i))));
					break;
				case bool:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteBoolean), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetBoolean), null, Expression.Constant(i))));
					break;
				case byte:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteNumber), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetByte), null, Expression.Constant(i))));
					break;
				case short:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteNumber), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetInt16), null, Expression.Constant(i))));
					break;
				case int:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteNumber), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetInt32), null, Expression.Constant(i))));
					break;
				case long:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteNumber), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetInt64), null, Expression.Constant(i))));
					break;
				case float:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteNumber), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetFloat), null, Expression.Constant(i))));
					break;
				case double:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteNumber), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetDouble), null, Expression.Constant(i))));
					break;
				case decimal:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteNumber), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetDecimal), null, Expression.Constant(i))));
					break;
				case string or char:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteString), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetString), null, Expression.Constant(i))));
					break;
				case Guid:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteString), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetGuid), null, Expression.Constant(i))));
					break;
				case DateTime:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteString), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetDateTime), null, Expression.Constant(i))));
					break;
				case DateTimeOffset:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteString), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Call(readerExpression, nameof(DbDataReader.GetDateTime), null, Expression.Constant(i))));
					break;
				case byte[]:
					expressions.Add(Expression.Call(writerExpression, nameof(Utf8JsonWriter.WriteBase64String), null,
						Expression.Constant(reader.GetName(i)),
						Expression.Convert(
							Expression.Convert(
								Expression.Call(readerExpression, nameof(DbDataReader.GetValue), null, Expression.Constant(i)),
								typeof(byte[])), typeof(ReadOnlySpan<byte>))));
					break;
				default:
					throw new NotSupportedException($"Type {reader.GetFieldType(i)} is not supported for serialization.");
			}

		return Expression.Lambda<Action<DbDataReader, Utf8JsonWriter>>(
			Expression.Block(expressions),
			readerExpression, writerExpression).Compile();
	}
	
	private static void WriteRecord(DbDataReader reader, Utf8JsonWriter writer,
		Action<DbDataReader, Utf8JsonWriter> serialize)
	{
		writer.WriteStartObject();
		serialize(reader, writer);
		writer.WriteEndObject();
	}
}