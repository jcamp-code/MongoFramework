using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

namespace MongoFramework.Infrastructure.Linq
{
	public static class ResultTransformers
	{
		/// <summary>
		/// Determines if a type is nullable (either Nullable&lt;T&gt; or a reference type).
		/// </summary>
		private static bool IsNullableType(Type type)
		{
			return Nullable.GetUnderlyingType(type) != null || !type.IsValueType;
		}

		public static Expression Transform(Expression expression, Type sourceType, bool isAsync)
		{
			if (expression is MethodCallExpression methodCallExpression)
			{
				var transformName = methodCallExpression.Method.Name;

				// For Max/Min with nullable types, use SingleOrDefault to return null for empty sequences
				// instead of throwing InvalidOperationException. This matches EF Core behavior.
				var useDefaultForEmptyMaxMin = (transformName == nameof(Queryable.Max) || transformName == nameof(Queryable.Min))
					&& IsNullableType(sourceType);

				if (isAsync)
				{
					var sourceParameter = Expression.Parameter(
						typeof(IAsyncEnumerable<>).MakeGenericType(sourceType),
						"source"
					);
					var cancellationTokenParameter = Expression.Parameter(
						typeof(CancellationToken),
						"cancellationToken"
					);

					var methodInfo = (transformName switch
					{
						nameof(Queryable.First) => MethodInfoCache.AsyncEnumerable.First_1,
						nameof(Queryable.FirstOrDefault) => MethodInfoCache.AsyncEnumerable.FirstOrDefault_1,

						nameof(Queryable.Single) => MethodInfoCache.AsyncEnumerable.Single_1,
						nameof(Queryable.SingleOrDefault) => MethodInfoCache.AsyncEnumerable.SingleOrDefault_1,

						nameof(Queryable.Count) => MethodInfoCache.AsyncEnumerable.SingleOrDefault_1,
						nameof(Queryable.Max) => useDefaultForEmptyMaxMin
							? MethodInfoCache.AsyncEnumerable.SingleOrDefault_1
							: MethodInfoCache.AsyncEnumerable.Single_1,
						nameof(Queryable.Min) => useDefaultForEmptyMaxMin
							? MethodInfoCache.AsyncEnumerable.SingleOrDefault_1
							: MethodInfoCache.AsyncEnumerable.Single_1,
						nameof(Queryable.Sum) => MethodInfoCache.AsyncEnumerable.SingleOrDefault_1,

						nameof(Queryable.Any) => MethodInfoCache.AsyncEnumerable.Any_1,

						_ => throw new InvalidOperationException($"No transform available for {transformName}")
					}).MakeGenericMethod(sourceType);

					return Expression.Lambda(
						Expression.Call(
							null,
							methodInfo,
							sourceParameter,
							cancellationTokenParameter
						),
						sourceParameter,
						cancellationTokenParameter
					);
				}
				else
				{
					var sourceParameter = Expression.Parameter(
						typeof(IEnumerable<>).MakeGenericType(sourceType),
						"source"
					);

					var methodInfo = (transformName switch
					{
						nameof(Queryable.First) => MethodInfoCache.Enumerable.First_1,
						nameof(Queryable.FirstOrDefault) => MethodInfoCache.Enumerable.FirstOrDefault_1,

						nameof(Queryable.Single) => MethodInfoCache.Enumerable.Single_1,
						nameof(Queryable.SingleOrDefault) => MethodInfoCache.Enumerable.SingleOrDefault_1,

						nameof(Queryable.Count) => MethodInfoCache.Enumerable.SingleOrDefault_1,
						nameof(Queryable.Max) => useDefaultForEmptyMaxMin
							? MethodInfoCache.Enumerable.SingleOrDefault_1
							: MethodInfoCache.Enumerable.Single_1,
						nameof(Queryable.Min) => useDefaultForEmptyMaxMin
							? MethodInfoCache.Enumerable.SingleOrDefault_1
							: MethodInfoCache.Enumerable.Single_1,
						nameof(Queryable.Sum) => MethodInfoCache.Enumerable.SingleOrDefault_1,

						nameof(Queryable.Any) => MethodInfoCache.Enumerable.Any_1,

						_ => throw new InvalidOperationException($"No transform available for {transformName}")
					}).MakeGenericMethod(sourceType);

					return Expression.Lambda(
						Expression.Call(
							null,
							methodInfo,
							sourceParameter
						),
						sourceParameter
					);
				}
			}

			throw new InvalidOperationException($"Result transformation unavailable for expression type {expression.NodeType}");
		}
	}
}