using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MongoFramework.Attributes;

namespace MongoFramework.Tests
{
	[TestClass]
	public class MongoDbContextTenantTests : TestBase
	{
		class DbSetModel : IHaveTenantId
		{
			public string Id { get; set; }
			public string TenantId { get; set; }
		}
		class BucketGroupModel
		{
			public string Name { get; set; }
		}
		class BucketSubEntity
		{
			public string Label { get; set; }
			public DateTime Date { get; set; }
		}

		class SecondModel
		{
			public string Id { get; set; }
		}

		class UndeclaredTenantModel : IHaveTenantId
		{
			public string Id { get; set; }
			public string TenantId { get; set; }
			public string Data { get; set; }
		}

		class MongoDbContextTestContext : MongoDbTenantContext
		{
			public MongoDbContextTestContext(IMongoDbConnection connection, string tenantId) : base(connection, tenantId) { }
			public MongoDbTenantSet<DbSetModel> DbSet { get; set; }
			[BucketSetOptions(5, nameof(BucketSubEntity.Date))]
			public MongoDbBucketSet<BucketGroupModel, BucketSubEntity> DbBucketSet { get; set; }
		}

		[TestMethod]
		public void ContextCreatedWithOptions()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				context.DbSet.Add(new DbSetModel());
				Assert.IsFalse(context.DbSet.Any());
				context.SaveChanges();
				Assert.IsTrue(context.DbSet.Any());
			}
		}

		[TestMethod]
		public void ContextCreatesDbSets()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				Assert.IsNotNull(context.DbSet);
				Assert.IsNotNull(context.DbBucketSet);
			}
		}

		[TestMethod]
		public void DbSetsHaveOptionsApplied()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				Assert.AreEqual(5, context.DbBucketSet.BucketSize);
			}
		}

		[TestMethod]
		public void GenericSetReturnsCorrectSet()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				Assert.IsInstanceOfType(context.Set<DbSetModel>(), typeof(MongoDbTenantSet<DbSetModel>));
			}
		}

		[TestMethod]
		public void GenericSetReturnsNewSet()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				Assert.IsInstanceOfType(context.Set<SecondModel>(), typeof(MongoDbSet<SecondModel>));
			}
		}

		[TestMethod]
		public void ContextSavesDbSets()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				context.DbSet.Add(new DbSetModel());
				Assert.IsFalse(context.DbSet.Any());
				context.SaveChanges();
				Assert.IsTrue(context.DbSet.Any());
				Assert.AreEqual(TestConfiguration.GetTenantId(), context.DbSet.First().TenantId);
			}
		}

		[TestMethod]
		public async Task ContextSavesDbSetsAsync()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				context.DbSet.Add(new DbSetModel());
				Assert.IsFalse(context.DbSet.Any());
				await context.SaveChangesAsync().ConfigureAwait(false);
				Assert.IsTrue(context.DbSet.Any());
				Assert.AreEqual(TestConfiguration.GetTenantId(), context.DbSet.First().TenantId);
			}
		}

		[TestMethod]
		public void SuccessfullyAttachUntrackedEntity()
		{
			var connection = TestConfiguration.GetConnection();
			var tenantId = TestConfiguration.GetTenantId();
			var context = new MongoDbTenantContext(connection, tenantId);
			var dbSet = new MongoDbTenantSet<DbSetModel>(context);

			var model = new DbSetModel
			{
				Id = "abcd"
			};

			dbSet.Add(model);

			context.SaveChanges();

			ResetMongoDb();

			context = new MongoDbTenantContext(connection, tenantId);
			dbSet = new MongoDbTenantSet<DbSetModel>(context);

			var result = dbSet.AsNoTracking().FirstOrDefault();

			context.Attach(result);

			Assert.AreEqual(MongoFramework.Infrastructure.EntityEntryState.NoChanges, context.ChangeTracker.GetEntry(result).State);
		}

		[TestMethod]
		public void SuccessfullyAttachUntrackedEntities()
		{
			var connection = TestConfiguration.GetConnection();
			var tenantId = TestConfiguration.GetTenantId();
			var context = new MongoDbTenantContext(connection, tenantId);
			var dbSet = new MongoDbTenantSet<DbSetModel>(context);

			var entities = new[] {
				new DbSetModel
				{
					Id = "abcd"
				},
				new DbSetModel
				{
					Id = "efgh"
				}
			};

			dbSet.AddRange(entities);

			context.SaveChanges();

			ResetMongoDb();

			context = new MongoDbTenantContext(connection, tenantId);
			dbSet = new MongoDbTenantSet<DbSetModel>(context);

			var result = dbSet.AsNoTracking().ToList();

			context.AttachRange(result);

			Assert.AreEqual(MongoFramework.Infrastructure.EntityEntryState.NoChanges, context.ChangeTracker.GetEntry(result[0]).State);
			Assert.AreEqual(MongoFramework.Infrastructure.EntityEntryState.NoChanges, context.ChangeTracker.GetEntry(result[1]).State);
		}

		[TestMethod]
		public void AttachRejectsMismatchedEntity()
		{
			var connection = TestConfiguration.GetConnection();
			var tenantId = TestConfiguration.GetTenantId();
			var context = new MongoDbTenantContext(connection, tenantId);
			var dbSet = new MongoDbTenantSet<DbSetModel>(context);

			var model = new DbSetModel
			{
				Id = "abcd"
			};

			dbSet.Add(model);

			context.SaveChanges();

			ResetMongoDb();

			context = new MongoDbTenantContext(connection, tenantId);
			dbSet = new MongoDbTenantSet<DbSetModel>(context);

			var result = dbSet.AsNoTracking().FirstOrDefault();
			result.TenantId = tenantId + "a";

			Assert.Throws<MultiTenantException>(() => context.Attach(result));
		}

		[TestMethod]
		public void AttachRejectsMismatchedEntities()
		{
			var connection = TestConfiguration.GetConnection();
			var tenantId = TestConfiguration.GetTenantId();
			var context = new MongoDbTenantContext(connection, tenantId);
			var dbSet = new MongoDbTenantSet<DbSetModel>(context);

			var entities = new[] {
				new DbSetModel
				{
					Id = "abcd"
				},
				new DbSetModel
				{
					Id = "efgh"
				}
			};

			dbSet.AddRange(entities);

			context.SaveChanges();

			ResetMongoDb();

			context = new MongoDbTenantContext(connection, tenantId);
			dbSet = new MongoDbTenantSet<DbSetModel>(context);

			var result = dbSet.AsNoTracking().ToList();
			result[0].TenantId = tenantId + "a";

			Assert.Throws<MultiTenantException>(() => context.AttachRange(result));
		}

		[TestMethod]
		public void DynamicSetReturnsTenantSetForTenantEntity()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				var set = context.Set<UndeclaredTenantModel>();
				Assert.IsInstanceOfType(set, typeof(MongoDbTenantSet<UndeclaredTenantModel>));
			}
		}

		[TestMethod]
		public void DynamicSetReturnsBasicSetForNonTenantEntity()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				var set = context.Set<SecondModel>();
				Assert.IsInstanceOfType(set, typeof(MongoDbSet<SecondModel>));
			}
		}

		[TestMethod]
		public void DynamicSetIsCachedPerType()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				var set1 = context.Set<UndeclaredTenantModel>();
				var set2 = context.Set<UndeclaredTenantModel>();
				Assert.AreSame(set1, set2);
			}
		}

		[TestMethod]
		public void DynamicTenantSetAutoSetsTenantIdOnAdd()
		{
			using (var context = new MongoDbContextTestContext(TestConfiguration.GetConnection(), TestConfiguration.GetTenantId()))
			{
				var set = context.Set<UndeclaredTenantModel>();
				var entity = new UndeclaredTenantModel { Data = "test" };
				set.Add(entity);
				Assert.AreEqual(TestConfiguration.GetTenantId(), entity.TenantId);
			}
		}

		[TestMethod]
		public void DynamicTenantSetFiltersQueriesByTenant()
		{
			var connection = TestConfiguration.GetConnection();
			var tenantId = TestConfiguration.GetTenantId();

			// Insert data for tenant A
			using (var contextA = new MongoDbContextTestContext(connection, tenantId))
			{
				var set = contextA.Set<UndeclaredTenantModel>();
				set.Add(new UndeclaredTenantModel { Data = "tenant-a-data" });
				contextA.SaveChanges();
			}

			// Insert data for tenant B using a different tenant ID
			using (var contextB = new MongoDbContextTestContext(TestConfiguration.GetConnection(), "other-tenant"))
			{
				var set = contextB.Set<UndeclaredTenantModel>();
				set.Add(new UndeclaredTenantModel { Data = "tenant-b-data" });
				contextB.SaveChanges();
			}

			// Query from tenant A context — should only see tenant A's data
			using (var contextA = new MongoDbContextTestContext(TestConfiguration.GetConnection(), tenantId))
			{
				var set = contextA.Set<UndeclaredTenantModel>();
				var results = set.ToList();
				Assert.IsTrue(results.All(r => r.TenantId == tenantId));
				Assert.IsTrue(results.Any(r => r.Data == "tenant-a-data"));
				Assert.IsFalse(results.Any(r => r.Data == "tenant-b-data"));
			}

			// Query from tenant B context — should only see tenant B's data
			using (var contextB = new MongoDbContextTestContext(TestConfiguration.GetConnection(), "other-tenant"))
			{
				var set = contextB.Set<UndeclaredTenantModel>();
				var results = set.ToList();
				Assert.IsTrue(results.All(r => r.TenantId == "other-tenant"));
				Assert.IsTrue(results.Any(r => r.Data == "tenant-b-data"));
				Assert.IsFalse(results.Any(r => r.Data == "tenant-a-data"));
			}
		}

		[TestMethod]
		public void DynamicTenantSetValidatesTenantOnUpdate()
		{
			var connection = TestConfiguration.GetConnection();
			var tenantId = TestConfiguration.GetTenantId();

			using (var context = new MongoDbContextTestContext(connection, tenantId))
			{
				var set = context.Set<UndeclaredTenantModel>();
				var entity = new UndeclaredTenantModel { Data = "original" };
				set.Add(entity);
				context.SaveChanges();

				// Tamper with the tenant ID
				entity.TenantId = "wrong-tenant";

				Assert.Throws<MultiTenantException>(() => set.Update(entity));
			}
		}

		[TestMethod]
		public void DynamicTenantSetValidatesTenantOnRemove()
		{
			var connection = TestConfiguration.GetConnection();
			var tenantId = TestConfiguration.GetTenantId();

			using (var context = new MongoDbContextTestContext(connection, tenantId))
			{
				var set = context.Set<UndeclaredTenantModel>();
				var entity = new UndeclaredTenantModel { Data = "to-remove" };
				set.Add(entity);
				context.SaveChanges();

				// Tamper with the tenant ID
				entity.TenantId = "wrong-tenant";

				Assert.Throws<MultiTenantException>(() => set.Remove(entity));
			}
		}

		[TestMethod]
		public void DynamicTenantSetSavesAndQueriesCorrectly()
		{
			var connection = TestConfiguration.GetConnection();
			var tenantId = TestConfiguration.GetTenantId();

			using (var context = new MongoDbContextTestContext(connection, tenantId))
			{
				var set = context.Set<UndeclaredTenantModel>();
				set.Add(new UndeclaredTenantModel { Data = "dynamic-save-test" });
				Assert.IsFalse(set.Any());
				context.SaveChanges();
				Assert.IsTrue(set.Any(m => m.Data == "dynamic-save-test"));
				Assert.AreEqual(tenantId, set.First().TenantId);
			}
		}

	}
}