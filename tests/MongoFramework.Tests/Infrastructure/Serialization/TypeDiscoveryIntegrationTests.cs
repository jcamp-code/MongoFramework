using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MongoFramework.Attributes;

namespace MongoFramework.Tests.Infrastructure.Serialization
{
	[TestClass]
	public class TypeDiscoveryIntegrationTests : TestBase
	{
		[RuntimeTypeDiscovery]
		public class RootKnownBaseModel
		{
			public string Id { get; set; }
			public string Description { get; set; }
		}

		public class UnknownChildToRootModel : RootKnownBaseModel
		{
			public string AdditionProperty { get; set; }
		}

		public class UnknownPropertyTypeModel
		{
			public string Id { get; set; }
			public object UnknownItem { get; set; }
		}

		public class UnknownPropertyTypeChildModel
		{
			public string Description { get; set; }
		}

		public class DictionaryWithListModel
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public Dictionary<string, object> Config { get; set; }
		}

		public class SimpleQueryModel
		{
			public string Id { get; set; }
			public string Name { get; set; }
		}

		[TestMethod]
		public void ReadAndWriteRootEntity()
		{
			var connection = TestConfiguration.GetConnection();
			var context = new MongoDbContext(connection);
			var dbSet = new MongoDbSet<RootKnownBaseModel>(context);

			var rootEntity = new RootKnownBaseModel
			{
				Description = "ReadAndWriteRootEntity-RootKnownBaseModel"
			};
			dbSet.Add(rootEntity);

			var childEntity = new UnknownChildToRootModel
			{
				Description = "ReadAndWriteRootEntity-UnknownChildToRootModel"
			};
			dbSet.Add(childEntity);

			context.SaveChanges();

			ResetMongoDb();
			dbSet = new MongoDbSet<RootKnownBaseModel>(context);

			var dbRootEntity = dbSet.Where(e => e.Id == rootEntity.Id).FirstOrDefault();
			Assert.IsNotNull(dbRootEntity);
			Assert.IsInstanceOfType(dbRootEntity, typeof(RootKnownBaseModel));

			var dbChildEntity = dbSet.Where(e => e.Id == childEntity.Id).FirstOrDefault();
			Assert.IsNotNull(dbChildEntity);
			Assert.IsInstanceOfType(dbChildEntity, typeof(UnknownChildToRootModel));
		}

		[TestMethod]
		public void ReadAndWriteUnknownPropertyTypeEntity()
		{
			var connection = TestConfiguration.GetConnection();
			var context = new MongoDbContext(connection);
			var dbSet = new MongoDbSet<UnknownPropertyTypeModel>(context);

			var entities = new[]
			{
				new UnknownPropertyTypeModel(),
				new UnknownPropertyTypeModel
				{
					UnknownItem = new UnknownPropertyTypeChildModel
					{
						Description = "UnknownPropertyTypeChildModel"
					}
				},
				new UnknownPropertyTypeModel
				{
					UnknownItem = new Dictionary<string, int>
					{
						{ "Age", 1 }
					}
				}
			};

			dbSet.AddRange(entities);
			context.SaveChanges();

			ResetMongoDb();
			dbSet = new MongoDbSet<UnknownPropertyTypeModel>(context);

			var dbEntities = dbSet.ToArray();
			Assert.IsNull(dbEntities[0].UnknownItem);
			Assert.IsInstanceOfType(dbEntities[1].UnknownItem, typeof(UnknownPropertyTypeChildModel));
			Assert.IsInstanceOfType(dbEntities[2].UnknownItem, typeof(Dictionary<string, object>));
		}

		[TestMethod]
		public void ReadWriteEntityWithListInDictionary()
		{
			// Regression: Storing a List<string> inside a Dictionary<string, object> property
			// and persisting to MongoDB must not corrupt the global BsonClassMap state.
			// Before the fix, TypeDiscoverySerializer registered List<string> as an entity type,
			// breaking LINQ query translation for all subsequent queries.
			var connection = TestConfiguration.GetConnection();
			var context = new MongoDbContext(connection);
			var dbSet = new MongoDbSet<DictionaryWithListModel>(context);

			var entity = new DictionaryWithListModel
			{
				Name = "ListInDictTest",
				Config = new Dictionary<string, object>
				{
					{ "eventType", "status_changed" },
					{ "toValues", new List<string> { "in_progress", "completed" } },
				}
			};

			dbSet.Add(entity);
			context.SaveChanges();

			ResetMongoDb();
			dbSet = new MongoDbSet<DictionaryWithListModel>(context);

			var result = dbSet.Where(e => e.Id == entity.Id).FirstOrDefault();
			Assert.IsNotNull(result);
			Assert.AreEqual("ListInDictTest", result.Name);
			Assert.AreEqual("status_changed", result.Config["eventType"]);

			// The list values should survive the round-trip (may come back as object[] or List<object>)
			var toValuesRaw = result.Config["toValues"];
			Assert.IsNotNull(toValuesRaw);
			var toValues = toValuesRaw is IEnumerable<object> enumerable
				? enumerable.ToArray()
				: (object[])toValuesRaw;
			Assert.AreEqual(2, toValues.Length);
			Assert.AreEqual("in_progress", toValues[0]?.ToString());
			Assert.AreEqual("completed", toValues[1]?.ToString());
		}

		[TestMethod]
		public void ListInDictionaryDoesNotBreakOtherEntityQueries()
		{
			// Regression: After writing an entity with List<string> in Dictionary<string, object>,
			// LINQ queries on a completely separate entity type must still work.
			var connection = TestConfiguration.GetConnection();
			var context = new MongoDbContext(connection);

			// First, write an entity with List<string> in a dictionary
			var dictDbSet = new MongoDbSet<DictionaryWithListModel>(context);
			dictDbSet.Add(new DictionaryWithListModel
			{
				Name = "CrossEntityTest",
				Config = new Dictionary<string, object>
				{
					{ "tags", new List<string> { "a", "b", "c" } },
				}
			});
			context.SaveChanges();

			// Then, write and query a separate entity type — this must not fail
			var simpleDbSet = new MongoDbSet<SimpleQueryModel>(context);
			simpleDbSet.Add(new SimpleQueryModel { Name = "SimpleTest" });
			context.SaveChanges();

			var result = simpleDbSet.Where(e => e.Name == "SimpleTest").FirstOrDefault();
			Assert.IsNotNull(result);
			Assert.AreEqual("SimpleTest", result.Name);
		}
	}
}
