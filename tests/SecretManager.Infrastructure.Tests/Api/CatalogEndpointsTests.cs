using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecretManager.ControlPlane.Application.Catalog;
using SecretManager.ControlPlane.Application.Authorization;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Catalog;
using SecretManager.Domain.Environments;
using SecretManager.Domain.Installations;
using SecretManager.Domain.Topology;
using SecretManager.Domain.Users;
using SecretManager.Infrastructure.Persistence;
using SecretManager.Infrastructure.Security;
using SecretManager.Shared.Contracts.Auth;
using SecretManager.Shared.Contracts.Catalog;

namespace SecretManager.Infrastructure.Tests.Api;

public sealed class CatalogEndpointsTests
{
    [Fact]
    public async Task GetApplications_ReturnsUnauthorized_WhenUserIsAnonymous()
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var response = await client.GetAsync("/api/v1/applications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApplicationCrud_Succeeds_WhenUserHasApplicationPermissions()
    {
        using var factory = new CatalogApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.ApplicationsRead,
            PermissionCatalog.ApplicationsWrite);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/applications",
            new CreateApplicationRequest
            {
                Name = "Trading API",
                Description = "Primary trade execution API"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdApplication = await createResponse.Content.ReadFromJsonAsync<ApplicationSummaryResponse>();
        Assert.NotNull(createdApplication);
        Assert.Equal("trading-api", createdApplication!.Slug);
        Assert.Equal("runtime-api", createdApplication.DefaultIntegrationMode);

        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/v1/applications/{createdApplication.ApplicationId}",
            new UpdateApplicationRequest
            {
                Name = "Trading API Core",
                Slug = "trading-api-core",
                Description = "Updated description",
                DefaultIntegrationMode = "json-file"
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedApplication = await updateResponse.Content.ReadFromJsonAsync<ApplicationSummaryResponse>();
        Assert.NotNull(updatedApplication);
        Assert.Equal("Trading API Core", updatedApplication!.Name);
        Assert.Equal("trading-api-core", updatedApplication.Slug);
        Assert.Equal("json-file", updatedApplication.DefaultIntegrationMode);

        var listResponse = await client.GetFromJsonAsync<List<ApplicationSummaryResponse>>("/api/v1/applications");
        Assert.NotNull(listResponse);
        Assert.Single(listResponse!);

        var deleteResponse = await client.DeleteAsync($"/api/v1/applications/{createdApplication.ApplicationId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var applicationsAfterDelete = await client.GetFromJsonAsync<List<ApplicationSummaryResponse>>("/api/v1/applications");
        Assert.NotNull(applicationsAfterDelete);
        Assert.Empty(applicationsAfterDelete!);
    }

    [Fact]
    public async Task NamespaceCrud_Succeeds_WhenUserHasNamespacePermissions()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.NamespacesRead,
            PermissionCatalog.NamespacesWrite);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/namespaces",
            new CreateNamespaceRequest
            {
                ApplicationId = applicationId,
                Name = "Core Settings",
                Path = "Trading:Core",
                Description = "Core runtime settings"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdNamespace = await createResponse.Content.ReadFromJsonAsync<NamespaceSummaryResponse>();
        Assert.NotNull(createdNamespace);
        Assert.Equal("Trading:Core", createdNamespace!.Path);

        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/v1/namespaces/{createdNamespace.NamespaceId}",
            new UpdateNamespaceRequest
            {
                Name = "Core Runtime",
                Path = "Trading:Runtime",
                Description = "Runtime settings"
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedNamespace = await updateResponse.Content.ReadFromJsonAsync<NamespaceSummaryResponse>();
        Assert.NotNull(updatedNamespace);
        Assert.Equal("Trading:Runtime", updatedNamespace!.Path);

        var listResponse = await client.GetFromJsonAsync<List<NamespaceSummaryResponse>>($"/api/v1/namespaces?applicationId={applicationId}");
        Assert.NotNull(listResponse);
        Assert.Single(listResponse!);

        var deleteResponse = await client.DeleteAsync($"/api/v1/namespaces/{createdNamespace.NamespaceId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ConfigItemCrud_Succeeds_AndPreservesSecretClassification_WhenUserHasConfigPermissions()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.ConfigReadMasked,
            PermissionCatalog.ConfigWriteDraft,
            PermissionCatalog.ConfigDeleteDraft);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/config-items",
            new CreateConfigItemRequest
            {
                NamespaceId = namespaceId,
                Key = "DbPassword",
                ValueType = "string",
                IsSecret = true,
                IsRequired = true,
                DefaultRolloutPolicy = "immediate",
                ValidationSchemaJson = "{\"minLength\":12}",
                Description = "Primary database password"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdConfigItem = await createResponse.Content.ReadFromJsonAsync<ConfigItemSummaryResponse>();
        Assert.NotNull(createdConfigItem);
        Assert.Equal(applicationId, createdConfigItem!.ApplicationId);
        Assert.Equal("Trading:Core:DbPassword", createdConfigItem.FullPath);
        Assert.True(createdConfigItem.IsSecret);

        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/v1/config-items/{createdConfigItem.ConfigItemId}",
            new UpdateConfigItemRequest
            {
                NamespaceId = namespaceId,
                Key = "DbPassword",
                ValueType = "string",
                IsSecret = false,
                IsRequired = true,
                DefaultRolloutPolicy = "manual-reload",
                ValidationSchemaJson = "{\"minLength\":16}",
                Description = "Updated metadata"
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedConfigItem = await updateResponse.Content.ReadFromJsonAsync<ConfigItemSummaryResponse>();
        Assert.NotNull(updatedConfigItem);
        Assert.False(updatedConfigItem!.IsSecret);
        Assert.Equal("manual-reload", updatedConfigItem.DefaultRolloutPolicy);

        var listResponse = await client.GetFromJsonAsync<List<ConfigItemSummaryResponse>>($"/api/v1/config-items?applicationId={applicationId}");
        Assert.NotNull(listResponse);
        Assert.Single(listResponse!);

        var deleteResponse = await client.DeleteAsync($"/api/v1/config-items/{createdConfigItem.ConfigItemId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var configItemsAfterDelete = await client.GetFromJsonAsync<List<ConfigItemSummaryResponse>>($"/api/v1/config-items?applicationId={applicationId}");
        Assert.NotNull(configItemsAfterDelete);
        Assert.Empty(configItemsAfterDelete!);
    }

    [Fact]
    public async Task CreateApplicationAssignment_Succeeds_WhenUserHasApplicationPermissions()
    {
        using var factory = new CatalogApiFactory();
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        var nodeGroupId = await factory.SeedNodeGroupAsync(environmentId, "Backend", "backend");
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.ApplicationsRead,
            PermissionCatalog.ApplicationsWrite);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/application-assignments",
            new CreateApplicationAssignmentRequest
            {
                ApplicationId = applicationId,
                EnvironmentId = environmentId,
                NodeGroupId = nodeGroupId,
                Enabled = true
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdAssignment = await createResponse.Content.ReadFromJsonAsync<ApplicationAssignmentResponse>();
        Assert.NotNull(createdAssignment);
        Assert.Equal(nodeGroupId, createdAssignment!.NodeGroupId);
        Assert.Null(createdAssignment.ManagedNodeId);

        var assignments = await client.GetFromJsonAsync<List<ApplicationAssignmentResponse>>($"/api/v1/application-assignments?applicationId={applicationId}");
        Assert.NotNull(assignments);

        var listedAssignment = Assert.Single(assignments!);
        Assert.Equal(createdAssignment.AssignmentId, listedAssignment.AssignmentId);
    }

    [Fact]
    public async Task AppSettingsImportPreview_ReportsExistingConfigItemConflicts()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
        var existingConfigItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "ApiKey",
            "Trading:Core:ApiKey",
            isSecret: true,
            valueType: "string");
        using var client = await factory.CreateAuthenticatedClientAsync(PermissionCatalog.ConfigReadMasked);

        var response = await client.PostAsJsonAsync(
            "/api/v1/imports/appsettings/preview",
            new
            {
                ApplicationId = applicationId,
                ScopeType = ResourceScopeType.Environment.ToString(),
                ScopeId = environmentId,
                JsonPayload = """
                {
                  "Trading": {
                    "Core": {
                      "ApiKey": "existing-secret",
                      "MaxRetries": 5
                    }
                  }
                }
                """
            });

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, responseBody);

        using var preview = JsonDocument.Parse(responseBody);
        Assert.Equal(1, preview.RootElement.GetProperty("conflictCount").GetInt32());

        var namespaces = preview.RootElement.GetProperty("namespaces").EnumerateArray().ToList();
        Assert.Single(namespaces);
        Assert.True(namespaces[0].GetProperty("exists").GetBoolean());
        Assert.Equal("Trading:Core", namespaces[0].GetProperty("path").GetString());

        var configItems = preview.RootElement.GetProperty("configItems").EnumerateArray().ToList();
        var existingItem = Assert.Single(configItems, x => x.GetProperty("fullPath").GetString() == "Trading:Core:ApiKey");
        Assert.Equal(existingConfigItemId, existingItem.GetProperty("existingConfigItemId").GetGuid());
        Assert.False(existingItem.GetProperty("hasExistingDraftValue").GetBoolean());
    }

    [Fact]
    public async Task AppSettingsImportApply_CreatesCatalogMetadata_AndDraftValues()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        using var client = await factory.CreateAuthenticatedClientAsync(PermissionCatalog.ConfigWriteDraft);

        var response = await client.PostAsJsonAsync(
            "/api/v1/imports/appsettings/apply",
            new
            {
                ApplicationId = applicationId,
                ScopeType = ResourceScopeType.Environment.ToString(),
                ScopeId = environmentId,
                JsonPayload = """
                {
                  "Trading": {
                    "Core": {
                      "ApiKey": "super-secret-value",
                      "MaxRetries": 3
                    }
                  },
                  "FeatureFlag": true
                }
                """,
                SecretFullPaths = new[] { "Trading:Core:ApiKey" },
                ChangeNote = "Initial appsettings import"
            });

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, responseBody);

        using var applyResponse = JsonDocument.Parse(responseBody);
        Assert.Equal(2, applyResponse.RootElement.GetProperty("namespaceCount").GetInt32());
        Assert.Equal(3, applyResponse.RootElement.GetProperty("configItemCount").GetInt32());
        Assert.Equal(3, applyResponse.RootElement.GetProperty("draftValueCount").GetInt32());
        Assert.Equal(2, applyResponse.RootElement.GetProperty("createdNamespaceCount").GetInt32());
        Assert.Equal(3, applyResponse.RootElement.GetProperty("createdConfigItemCount").GetInt32());
        Assert.Equal(3, applyResponse.RootElement.GetProperty("createdDraftValueCount").GetInt32());
        Assert.Equal(0, applyResponse.RootElement.GetProperty("updatedDraftValueCount").GetInt32());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
        var configItems = await dbContext.ConfigItems
            .AsNoTracking()
            .Where(x => x.ApplicationId == applicationId)
            .OrderBy(x => x.FullPath)
            .ToListAsync();

        Assert.Equal(3, configItems.Count);
        Assert.Contains(configItems, x => x.FullPath == "FeatureFlag");
        Assert.Contains(configItems, x => x.FullPath == "Trading:Core:ApiKey" && x.IsSecret);
        Assert.Contains(configItems, x => x.FullPath == "Trading:Core:MaxRetries" && x.ValueType == "integer");

        var namespaces = await dbContext.Namespaces
            .AsNoTracking()
            .Where(x => x.ApplicationId == applicationId)
            .OrderBy(x => x.Path)
            .ToListAsync();

        Assert.Equal(2, namespaces.Count);
        Assert.Contains(namespaces, x => x.Path == string.Empty);
        Assert.Contains(namespaces, x => x.Path == "Trading:Core");

        var configItemIds = configItems.Select(x => x.Id).ToList();
        var draftValuesProperty = dbContext.GetType().GetProperty("DraftValues");
        Assert.NotNull(draftValuesProperty);

        var draftValues = ((System.Collections.IEnumerable?)draftValuesProperty!.GetValue(dbContext))
            ?.Cast<object>()
            .Where(x =>
            {
                var draftValueType = x.GetType();
                var configItemId = (Guid)draftValueType.GetProperty("ConfigItemId")!.GetValue(x)!;
                var scopeType = (ResourceScopeType)draftValueType.GetProperty("ScopeType")!.GetValue(x)!;
                var scopeId = (Guid)draftValueType.GetProperty("ScopeId")!.GetValue(x)!;
                return configItemIds.Contains(configItemId)
                    && scopeType == ResourceScopeType.Environment
                    && scopeId == environmentId;
            })
            .ToList();

        Assert.NotNull(draftValues);

        Assert.Equal(3, draftValues!.Count);

        var apiKeyConfigItem = Assert.Single(configItems, x => x.FullPath == "Trading:Core:ApiKey");
        var apiKeyDraftValue = Assert.Single(
            draftValues,
            x => (Guid)x.GetType().GetProperty("ConfigItemId")!.GetValue(x)! == apiKeyConfigItem.Id);
        Assert.True((bool)apiKeyDraftValue.GetType().GetProperty("IsSecret")!.GetValue(apiKeyDraftValue)!);
        Assert.NotEqual("\"super-secret-value\"", (string)apiKeyDraftValue.GetType().GetProperty("ValueJson")!.GetValue(apiKeyDraftValue)!);
        Assert.Equal("Initial appsettings import", (string)apiKeyDraftValue.GetType().GetProperty("ChangeNote")!.GetValue(apiKeyDraftValue)!);
    }

    [Fact]
    public async Task DraftValueCrud_SupportsAllDocumentedScopes_AndEncryptsSecrets()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        var nodeGroupId = await factory.SeedNodeGroupAsync(environmentId, "Backend", "backend");
        var managedNodeId = await factory.SeedManagedNodeAsync(environmentId, nodeGroupId, "Node 01", "node01.local");
        var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
        var configItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "ApiKey",
            "Trading:Core:ApiKey",
            isSecret: true,
            valueType: "string");
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.ConfigReadMasked,
            PermissionCatalog.ConfigWriteDraft);

        var requests = new[]
        {
            new { ScopeType = ResourceScopeType.Application.ToString(), ScopeId = applicationId, ValueJson = "\"app-secret\"" },
            new { ScopeType = ResourceScopeType.Environment.ToString(), ScopeId = environmentId, ValueJson = "\"env-secret\"" },
            new { ScopeType = ResourceScopeType.NodeGroup.ToString(), ScopeId = nodeGroupId, ValueJson = "\"group-secret\"" },
            new { ScopeType = ResourceScopeType.ManagedNode.ToString(), ScopeId = managedNodeId, ValueJson = "\"node-secret\"" },
            new { ScopeType = ResourceScopeType.EmergencyOverride.ToString(), ScopeId = Installation.SingletonId, ValueJson = "\"break-glass-secret\"" }
        };

        foreach (var request in requests)
        {
            var createResponse = await client.PostAsJsonAsync(
                "/api/v1/draft-values",
                new
                {
                    ConfigItemId = configItemId,
                    request.ScopeType,
                    request.ScopeId,
                    request.ValueJson,
                    ChangeNote = $"Created {request.ScopeType} draft"
                });

            var responseBody = await createResponse.Content.ReadAsStringAsync();
            Assert.True(createResponse.StatusCode == HttpStatusCode.Created, responseBody);

            using var createdDraft = JsonDocument.Parse(responseBody);
            Assert.Equal(configItemId, createdDraft.RootElement.GetProperty("configItemId").GetGuid());
            Assert.Equal(request.ScopeType, createdDraft.RootElement.GetProperty("scopeType").GetString());
            Assert.Equal(request.ScopeId, createdDraft.RootElement.GetProperty("scopeId").GetGuid());
            Assert.True(createdDraft.RootElement.GetProperty("isSecret").GetBoolean());
            Assert.True(createdDraft.RootElement.GetProperty("isValueMasked").GetBoolean());
            Assert.True(createdDraft.RootElement.GetProperty("valueJson").ValueKind == JsonValueKind.Null);
        }

        var listResponse = await client.GetAsync($"/api/v1/draft-values?configItemId={configItemId}");
        var listResponseBody = await listResponse.Content.ReadAsStringAsync();
        Assert.True(listResponse.StatusCode == HttpStatusCode.OK, listResponseBody);

        using var listedDrafts = JsonDocument.Parse(listResponseBody);
        var draftItems = listedDrafts.RootElement.EnumerateArray().ToList();
        Assert.Equal(5, draftItems.Count);
        Assert.All(draftItems, draftItem =>
        {
            Assert.True(draftItem.GetProperty("isSecret").GetBoolean());
            Assert.True(draftItem.GetProperty("isValueMasked").GetBoolean());
            Assert.True(draftItem.GetProperty("valueJson").ValueKind == JsonValueKind.Null);
        });

        var listedScopeTypes = draftItems
            .Select(x => x.GetProperty("scopeType").GetString()!)
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(
            requests.Select(x => x.ScopeType).OrderBy(x => x).ToList(),
            listedScopeTypes);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
        var draftValuesProperty = dbContext.GetType().GetProperty("DraftValues");
        Assert.NotNull(draftValuesProperty);

        var persistedDraftValues = ((System.Collections.IEnumerable?)draftValuesProperty!.GetValue(dbContext))
            ?.Cast<object>()
            .Where(x => (Guid)x.GetType().GetProperty("ConfigItemId")!.GetValue(x)! == configItemId)
            .ToList();

        Assert.NotNull(persistedDraftValues);
        Assert.Equal(5, persistedDraftValues!.Count);

        var storedPayloads = persistedDraftValues
            .Select(x => (string)x.GetType().GetProperty("ValueJson")!.GetValue(x)!)
            .ToList();

        Assert.DoesNotContain("\"app-secret\"", storedPayloads);
        Assert.DoesNotContain("\"env-secret\"", storedPayloads);
        Assert.DoesNotContain("\"group-secret\"", storedPayloads);
        Assert.DoesNotContain("\"node-secret\"", storedPayloads);
        Assert.DoesNotContain("\"break-glass-secret\"", storedPayloads);
    }

    [Fact]
    public async Task DraftValueCrud_UpdatesDeletesAndAuditsNonSecretDrafts()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
        var configItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "MaxRetries",
            "Trading:Core:MaxRetries",
            isSecret: false,
            valueType: "integer");
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.ConfigReadMasked,
            PermissionCatalog.ConfigWriteDraft,
            PermissionCatalog.ConfigDeleteDraft);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/draft-values",
            new
            {
                ConfigItemId = configItemId,
                ScopeType = ResourceScopeType.Application.ToString(),
                ScopeId = applicationId,
                ValueJson = "3",
                ChangeNote = "Initial retries"
            });

        var createResponseBody = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.StatusCode == HttpStatusCode.Created, createResponseBody);

        using var createdDraft = JsonDocument.Parse(createResponseBody);
        var draftValueId = createdDraft.RootElement.GetProperty("draftValueId").GetGuid();
        Assert.False(createdDraft.RootElement.GetProperty("isSecret").GetBoolean());
        Assert.False(createdDraft.RootElement.GetProperty("isValueMasked").GetBoolean());
        Assert.Equal("3", createdDraft.RootElement.GetProperty("valueJson").GetString());

        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/v1/draft-values/{draftValueId}",
            new
            {
                ValueJson = "4",
                ChangeNote = "Tuned retries"
            });

        var updateResponseBody = await updateResponse.Content.ReadAsStringAsync();
        Assert.True(updateResponse.StatusCode == HttpStatusCode.OK, updateResponseBody);

        using var updatedDraft = JsonDocument.Parse(updateResponseBody);
        Assert.Equal("4", updatedDraft.RootElement.GetProperty("valueJson").GetString());
        Assert.Equal("Tuned retries", updatedDraft.RootElement.GetProperty("changeNote").GetString());

        var listResponse = await client.GetAsync(
            $"/api/v1/draft-values?configItemId={configItemId}&scopeType={ResourceScopeType.Application}&scopeId={applicationId}");
        var listResponseBody = await listResponse.Content.ReadAsStringAsync();
        Assert.True(listResponse.StatusCode == HttpStatusCode.OK, listResponseBody);

        using var listedDrafts = JsonDocument.Parse(listResponseBody);
        var listedDraft = Assert.Single(listedDrafts.RootElement.EnumerateArray().ToList());
        Assert.Equal(draftValueId, listedDraft.GetProperty("draftValueId").GetGuid());
        Assert.Equal("4", listedDraft.GetProperty("valueJson").GetString());

        var deleteResponse = await client.DeleteAsync($"/api/v1/draft-values/{draftValueId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDeleteResponse = await client.GetAsync(
            $"/api/v1/draft-values?configItemId={configItemId}&scopeType={ResourceScopeType.Application}&scopeId={applicationId}");
        var afterDeleteResponseBody = await afterDeleteResponse.Content.ReadAsStringAsync();
        Assert.True(afterDeleteResponse.StatusCode == HttpStatusCode.OK, afterDeleteResponseBody);

        using var draftsAfterDelete = JsonDocument.Parse(afterDeleteResponseBody);
        Assert.Empty(draftsAfterDelete.RootElement.EnumerateArray());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
        var auditEvents = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.TargetIdentifier == draftValueId.ToString())
            .Select(x => x.Action)
            .ToListAsync();

        Assert.Contains("draftValue.created", auditEvents);
        Assert.Contains("draftValue.updated", auditEvents);
        Assert.Contains("draftValue.deleted", auditEvents);
    }

    [Fact]
    public async Task EffectivePreview_ResolvesDocumentedPrecedence_AndIndicatesSourceScope()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        var nodeGroupId = await factory.SeedNodeGroupAsync(environmentId, "Backend", "backend");
        var managedNodeId = await factory.SeedManagedNodeAsync(environmentId, nodeGroupId, "Node 01", "node01.local");
        var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
        var maxRetriesConfigItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "MaxRetries",
            "Trading:Core:MaxRetries",
            isSecret: false,
            valueType: "integer");
        var apiKeyConfigItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "ApiKey",
            "Trading:Core:ApiKey",
            isSecret: true,
            valueType: "string");
        var featureFlagConfigItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "FeatureFlag",
            "Trading:Core:FeatureFlag",
            isSecret: false,
            valueType: "boolean");

        await factory.SeedApplicationAssignmentAsync(applicationId, environmentId, nodeGroupId: nodeGroupId);
        await factory.SeedDraftValueAsync(maxRetriesConfigItemId, ResourceScopeType.Application, applicationId, "2", isSecret: false);
        await factory.SeedDraftValueAsync(maxRetriesConfigItemId, ResourceScopeType.Environment, environmentId, "3", isSecret: false);
        await factory.SeedDraftValueAsync(maxRetriesConfigItemId, ResourceScopeType.NodeGroup, nodeGroupId, "4", isSecret: false);
        await factory.SeedDraftValueAsync(maxRetriesConfigItemId, ResourceScopeType.ManagedNode, managedNodeId, "5", isSecret: false);
        await factory.SeedDraftValueAsync(maxRetriesConfigItemId, ResourceScopeType.EmergencyOverride, Installation.SingletonId, "9", isSecret: false);
        await factory.SeedDraftValueAsync(apiKeyConfigItemId, ResourceScopeType.Application, applicationId, "\"app-secret\"", isSecret: true);
        await factory.SeedDraftValueAsync(apiKeyConfigItemId, ResourceScopeType.NodeGroup, nodeGroupId, "\"group-secret\"", isSecret: true);
        await factory.SeedDraftValueAsync(featureFlagConfigItemId, ResourceScopeType.Environment, environmentId, "true", isSecret: false);

        using var client = await factory.CreateAuthenticatedClientAsync(PermissionCatalog.ConfigReadMasked);

        var response = await client.GetAsync(
            $"/api/v1/effective-snapshots/preview?applicationId={applicationId}&environmentId={environmentId}&managedNodeId={managedNodeId}");

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, responseBody);

        using var preview = JsonDocument.Parse(responseBody);
        Assert.Equal(applicationId, preview.RootElement.GetProperty("applicationId").GetGuid());
        Assert.Equal(environmentId, preview.RootElement.GetProperty("environmentId").GetGuid());
        Assert.Equal(managedNodeId, preview.RootElement.GetProperty("managedNodeId").GetGuid());
        Assert.Equal(nodeGroupId, preview.RootElement.GetProperty("nodeGroupId").GetGuid());
        Assert.Equal(3, preview.RootElement.GetProperty("itemCount").GetInt32());

        var items = preview.RootElement.GetProperty("items").EnumerateArray().ToList();
        var maxRetries = Assert.Single(items, x => x.GetProperty("fullPath").GetString() == "Trading:Core:MaxRetries");
        Assert.Equal("9", maxRetries.GetProperty("valueJson").GetString());
        Assert.False(maxRetries.GetProperty("isSecret").GetBoolean());
        Assert.False(maxRetries.GetProperty("isValueMasked").GetBoolean());
        Assert.Equal(ResourceScopeType.EmergencyOverride.ToString(), maxRetries.GetProperty("sourceScopeType").GetString());
        Assert.Equal(Installation.SingletonId, maxRetries.GetProperty("sourceScopeId").GetGuid());

        var apiKey = Assert.Single(items, x => x.GetProperty("fullPath").GetString() == "Trading:Core:ApiKey");
        Assert.True(apiKey.GetProperty("isSecret").GetBoolean());
        Assert.True(apiKey.GetProperty("isValueMasked").GetBoolean());
        Assert.True(apiKey.GetProperty("valueJson").ValueKind == JsonValueKind.Null);
        Assert.Equal(ResourceScopeType.NodeGroup.ToString(), apiKey.GetProperty("sourceScopeType").GetString());
        Assert.Equal(nodeGroupId, apiKey.GetProperty("sourceScopeId").GetGuid());

        var featureFlag = Assert.Single(items, x => x.GetProperty("fullPath").GetString() == "Trading:Core:FeatureFlag");
        Assert.Equal("true", featureFlag.GetProperty("valueJson").GetString());
        Assert.Equal(ResourceScopeType.Environment.ToString(), featureFlag.GetProperty("sourceScopeType").GetString());
        Assert.Equal(environmentId, featureFlag.GetProperty("sourceScopeId").GetGuid());
    }

    [Fact]
    public async Task PublishPipeline_CreatesImmutableVersionRecords_WithoutOverwritingHistory()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
        var maxRetriesConfigItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "MaxRetries",
            "Trading:Core:MaxRetries",
            isSecret: false,
            valueType: "integer");
        var apiKeyConfigItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "ApiKey",
            "Trading:Core:ApiKey",
            isSecret: true,
            valueType: "string");

        await factory.SeedDraftValueAsync(maxRetriesConfigItemId, ResourceScopeType.Application, applicationId, "3", isSecret: false);
        await factory.SeedDraftValueAsync(apiKeyConfigItemId, ResourceScopeType.Environment, environmentId, "\"super-secret-value\"", isSecret: true);

        using var client = await factory.CreateAuthenticatedClientAsync(PermissionCatalog.ConfigPublish);

        var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/publishes",
            new CreatePublishRequest
            {
                ApplicationId = applicationId,
                EnvironmentId = environmentId,
                ChangeSummary = "Initial publish",
                RolloutPolicy = "manual-reload"
            });

        var firstResponseBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.True(firstResponse.StatusCode == HttpStatusCode.OK, firstResponseBody);
        var firstPublish = await firstResponse.Content.ReadFromJsonAsync<CreatePublishResponse>();
        Assert.NotNull(firstPublish);
        Assert.Equal(1, firstPublish!.PublishedVersion.VersionNumber);
        Assert.Equal("manual-reload", firstPublish.PublishedVersion.RolloutPolicy);

        using (var firstScope = factory.Services.CreateScope())
        {
            var dbContext = firstScope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            var publishOperation = await dbContext.PublishOperations.AsNoTracking().SingleAsync();
            var publishedVersion = await dbContext.PublishedVersions.AsNoTracking().SingleAsync();

            Assert.Equal(firstPublish.PublishOperation.PublishOperationId, publishOperation.Id);
            Assert.Equal("Completed", publishOperation.Status);
            Assert.Equal(firstPublish.PublishedVersion.PublishedVersionId, publishedVersion.Id);
            Assert.Equal(1, publishedVersion.VersionNumber);
            Assert.Equal("manual-reload", publishedVersion.RolloutPolicy);
            Assert.DoesNotContain("super-secret-value", publishedVersion.PayloadJson, StringComparison.Ordinal);
        }

        var secondResponse = await client.PostAsJsonAsync(
            "/api/v1/publishes",
            new CreatePublishRequest
            {
                ApplicationId = applicationId,
                EnvironmentId = environmentId,
                ChangeSummary = "Repeated publish",
                RolloutPolicy = "manual-reload"
            });

        var secondResponseBody = await secondResponse.Content.ReadAsStringAsync();
        Assert.True(secondResponse.StatusCode == HttpStatusCode.OK, secondResponseBody);
        var secondPublish = await secondResponse.Content.ReadFromJsonAsync<CreatePublishResponse>();
        Assert.NotNull(secondPublish);
        Assert.Equal(2, secondPublish!.PublishedVersion.VersionNumber);

        using var secondScope = factory.Services.CreateScope();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
        var publishOperations = await secondDbContext.PublishOperations
            .AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
        var publishedVersions = await secondDbContext.PublishedVersions
            .AsNoTracking()
            .OrderBy(x => x.VersionNumber)
            .ToListAsync();

        Assert.Equal(2, publishOperations.Count);
        Assert.Equal(2, publishedVersions.Count);
        Assert.Equal(1, publishedVersions[0].VersionNumber);
        Assert.Equal(2, publishedVersions[1].VersionNumber);
        Assert.Equal(publishedVersions[0].Id, publishedVersions[1].SupersedesVersionId);
        Assert.Equal(publishedVersions[0].ContentHash, publishedVersions[1].ContentHash);
        Assert.Equal(publishedVersions[0].PayloadJson, publishedVersions[1].PayloadJson);

        var publishAuditEvents = await secondDbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.Action == "publish.created")
            .ToListAsync();

        Assert.Equal(2, publishAuditEvents.Count);
    }

    [Fact]
    public async Task PublishPipeline_IncludesInstallationScopedImportedDraftValues()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.ConfigWriteDraft,
            PermissionCatalog.ConfigPublish);

        var importResponse = await client.PostAsJsonAsync(
            "/api/v1/imports/appsettings/apply",
            new
            {
                ApplicationId = applicationId,
                ScopeType = ResourceScopeType.Installation.ToString(),
                ScopeId = Installation.SingletonId,
                JsonPayload = """
                {
                  "Trading": {
                    "Core": {
                      "ApiKey": "bootstrap-secret",
                      "MaxRetries": 5
                    }
                  }
                }
                """,
                SecretFullPaths = new[] { "Trading:Core:ApiKey" },
                ChangeNote = "Imported baseline appsettings payload"
            });

        var importResponseBody = await importResponse.Content.ReadAsStringAsync();
        Assert.True(importResponse.StatusCode == HttpStatusCode.OK, importResponseBody);

        var publishResponse = await client.PostAsJsonAsync(
            "/api/v1/publishes",
            new CreatePublishRequest
            {
                ApplicationId = applicationId,
                EnvironmentId = environmentId,
                ChangeSummary = "Publish imported baseline",
                RolloutPolicy = "immediate"
            });

        var publishResponseBody = await publishResponse.Content.ReadAsStringAsync();
        Assert.True(publishResponse.StatusCode == HttpStatusCode.OK, publishResponseBody);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
        var publishedVersion = await dbContext.PublishedVersions.AsNoTracking().SingleAsync();

        var payload = JsonSerializer.Deserialize<PublishedVersionPayloadDocument>(publishedVersion.PayloadJson);

        Assert.NotNull(payload);
        var importedValue = Assert.Single(
            payload!.DraftValues,
            x => x.FullPath == "Trading:Core:MaxRetries");

        Assert.Equal(ResourceScopeType.Installation.ToString(), importedValue.ScopeType);
        Assert.Equal(Installation.SingletonId, importedValue.ScopeId);
        Assert.Equal("5", importedValue.ValueJson);
    }

    [Fact]
    public async Task PublishedVersionDiff_AndRollback_ExposeChanges_AndRecreateHistoricalContent()
    {
        using var factory = new CatalogApiFactory();
        var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
        var maxRetriesConfigItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "MaxRetries",
            "Trading:Core:MaxRetries",
            isSecret: false,
            valueType: "integer");
        var featureFlagConfigItemId = await factory.SeedConfigItemAsync(
            applicationId,
            namespaceId,
            "FeatureFlag",
            "Trading:Core:FeatureFlag",
            isSecret: false,
            valueType: "boolean");

        await factory.SeedDraftValueAsync(maxRetriesConfigItemId, ResourceScopeType.Application, applicationId, "3", isSecret: false);

        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.ConfigReadMasked,
            PermissionCatalog.ConfigWriteDraft,
            PermissionCatalog.ConfigPublish,
            PermissionCatalog.ConfigRollback);

        var versionAResponse = await client.PostAsJsonAsync(
            "/api/v1/publishes",
            new CreatePublishRequest
            {
                ApplicationId = applicationId,
                EnvironmentId = environmentId,
                ChangeSummary = "Publish version A",
                RolloutPolicy = "immediate"
            });
        var versionABody = await versionAResponse.Content.ReadAsStringAsync();
        Assert.True(versionAResponse.StatusCode == HttpStatusCode.OK, versionABody);
        var versionAPublish = await versionAResponse.Content.ReadFromJsonAsync<CreatePublishResponse>();
        Assert.NotNull(versionAPublish);

        await factory.UpdateDraftValueAsync(maxRetriesConfigItemId, ResourceScopeType.Application, applicationId, "5", isSecret: false);
        await factory.SeedDraftValueAsync(featureFlagConfigItemId, ResourceScopeType.Environment, environmentId, "true", isSecret: false);

        var versionBResponse = await client.PostAsJsonAsync(
            "/api/v1/publishes",
            new CreatePublishRequest
            {
                ApplicationId = applicationId,
                EnvironmentId = environmentId,
                ChangeSummary = "Publish version B",
                RolloutPolicy = "immediate"
            });
        var versionBBody = await versionBResponse.Content.ReadAsStringAsync();
        Assert.True(versionBResponse.StatusCode == HttpStatusCode.OK, versionBBody);
        var versionBPublish = await versionBResponse.Content.ReadFromJsonAsync<CreatePublishResponse>();
        Assert.NotNull(versionBPublish);

        var versionsResponse = await client.GetAsync(
            $"/api/v1/published-versions?applicationId={applicationId}&environmentId={environmentId}");
        var versionsBody = await versionsResponse.Content.ReadAsStringAsync();
        Assert.True(versionsResponse.StatusCode == HttpStatusCode.OK, versionsBody);
        var publishedVersions = await versionsResponse.Content.ReadFromJsonAsync<List<PublishedVersionResponse>>();
        Assert.NotNull(publishedVersions);
        Assert.Equal([2, 1], publishedVersions!.Select(x => x.VersionNumber).ToArray());

        var diffResponse = await client.GetAsync(
            $"/api/v1/published-versions/{versionBPublish!.PublishedVersion.PublishedVersionId}/diff?compareToVersionId={versionAPublish!.PublishedVersion.PublishedVersionId}");
        var diffBody = await diffResponse.Content.ReadAsStringAsync();
        Assert.True(diffResponse.StatusCode == HttpStatusCode.OK, diffBody);
        using (var diffDocument = JsonDocument.Parse(diffBody))
        {
            Assert.Equal(2, diffDocument.RootElement.GetProperty("changeCount").GetInt32());
            var changes = diffDocument.RootElement.GetProperty("changes").EnumerateArray().ToList();

            var maxRetriesChange = Assert.Single(changes, x => x.GetProperty("fullPath").GetString() == "Trading:Core:MaxRetries");
            Assert.Equal("Modified", maxRetriesChange.GetProperty("changeType").GetString());
            Assert.Equal("3", maxRetriesChange.GetProperty("previousValueJson").GetString());
            Assert.Equal("5", maxRetriesChange.GetProperty("currentValueJson").GetString());

            var featureFlagChange = Assert.Single(changes, x => x.GetProperty("fullPath").GetString() == "Trading:Core:FeatureFlag");
            Assert.Equal("Added", featureFlagChange.GetProperty("changeType").GetString());
            Assert.True(featureFlagChange.GetProperty("previousValueJson").ValueKind == JsonValueKind.Null);
            Assert.Equal("true", featureFlagChange.GetProperty("currentValueJson").GetString());
        }

        var rollbackResponse = await client.PostAsJsonAsync(
            $"/api/v1/published-versions/{versionAPublish.PublishedVersion.PublishedVersionId}/rollback",
            new CreateRollbackRequest
            {
                ChangeSummary = "Rollback to version A"
            });
        var rollbackBody = await rollbackResponse.Content.ReadAsStringAsync();
        Assert.True(rollbackResponse.StatusCode == HttpStatusCode.OK, rollbackBody);
        var rollback = await rollbackResponse.Content.ReadFromJsonAsync<RollbackPublishedVersionResponse>();
        Assert.NotNull(rollback);
        Assert.Equal(3, rollback!.PublishedVersion.VersionNumber);

        var rollbackDiffResponse = await client.GetAsync(
            $"/api/v1/published-versions/{rollback.PublishedVersion.PublishedVersionId}/diff?compareToVersionId={versionAPublish.PublishedVersion.PublishedVersionId}");
        var rollbackDiffBody = await rollbackDiffResponse.Content.ReadAsStringAsync();
        Assert.True(rollbackDiffResponse.StatusCode == HttpStatusCode.OK, rollbackDiffBody);
        using (var rollbackDiff = JsonDocument.Parse(rollbackDiffBody))
        {
            Assert.Equal(0, rollbackDiff.RootElement.GetProperty("changeCount").GetInt32());
            Assert.False(rollbackDiff.RootElement.GetProperty("rolloutPolicyChanged").GetBoolean());
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
        var storedPublishedVersions = await dbContext.PublishedVersions
            .AsNoTracking()
            .OrderBy(x => x.VersionNumber)
            .ToListAsync();

        Assert.Equal(3, storedPublishedVersions.Count);
        Assert.Equal(storedPublishedVersions[0].PayloadJson, storedPublishedVersions[2].PayloadJson);

        var rollbackAuditEvents = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.Action == "rollback.created")
            .ToListAsync();

        Assert.Single(rollbackAuditEvents);
    }

    private sealed class CatalogApiFactory : WebApplicationFactory<Program>
    {
        private const string TestPassword = "Passw0rd!Passw0rd!";
        private readonly string databaseName = $"secretmanager-catalog-tests-{Guid.NewGuid():N}";

        public CatalogApiFactory()
        {
            System.Environment.SetEnvironmentVariable(
                "ConnectionStrings__Postgres",
                "Host=localhost;Port=5432;Database=secretmanager_test;Username=secretmanager;Password=secretmanager");
            System.Environment.SetEnvironmentVariable(
                "Infrastructure__Redis__Configuration",
                "localhost:6380,password=secretmanager,abortConnect=false");
            System.Environment.SetEnvironmentVariable(
                "Infrastructure__Redis__InstanceName",
                "secretmanager:");
            System.Environment.SetEnvironmentVariable("Telemetry__EnableConsoleExporter", "false");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=secretmanager_test;Username=secretmanager;Password=secretmanager",
                    ["Infrastructure:Redis:Configuration"] = "localhost:6380,password=secretmanager,abortConnect=false",
                    ["Infrastructure:Redis:InstanceName"] = "secretmanager:",
                    ["Telemetry:EnableConsoleExporter"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<SecretManagerDbContext>));
                services.RemoveAll(typeof(DbContextOptions<SecretManagerDbContext>));
                services.RemoveAll(typeof(SecretManagerDbContext));
                services.AddDbContext<SecretManagerDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        }

        public async Task<HttpClient> CreateAuthenticatedClientAsync(params string[] permissions)
        {
            await SeedUserAsync(permissions);

            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });

            var loginResponse = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginRequest
                {
                    Username = "operator",
                    Password = TestPassword
                });

            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            return client;
        }

        public async Task<Guid> SeedEnvironmentAsync(string name, string slug)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var environment = new EnvironmentDefinition
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = slug,
                Description = string.Empty,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.Environments.Add(environment);
            await dbContext.SaveChangesAsync();
            return environment.Id;
        }

        public async Task<Guid> SeedNodeGroupAsync(Guid environmentId, string name, string slug)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var nodeGroup = new NodeGroupDefinition
            {
                Id = Guid.NewGuid(),
                EnvironmentId = environmentId,
                Name = name,
                Slug = slug,
                Description = string.Empty,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.NodeGroups.Add(nodeGroup);
            await dbContext.SaveChangesAsync();
            return nodeGroup.Id;
        }

        public async Task<Guid> SeedManagedNodeAsync(Guid environmentId, Guid? nodeGroupId, string name, string hostname)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var managedNode = new ManagedNodeRecord
            {
                Id = Guid.NewGuid(),
                EnvironmentId = environmentId,
                NodeGroupId = nodeGroupId,
                Name = name,
                Hostname = hostname,
                Platform = "linux",
                Status = "Online",
                AgentVersion = "1.0.0",
                RolloutPolicyDefault = "immediate",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.ManagedNodes.Add(managedNode);
            await dbContext.SaveChangesAsync();
            return managedNode.Id;
        }

        public async Task<Guid> SeedApplicationAssignmentAsync(
            Guid applicationId,
            Guid environmentId,
            Guid? nodeGroupId = null,
            Guid? managedNodeId = null,
            bool enabled = true)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var assignment = new ApplicationAssignment
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                EnvironmentId = environmentId,
                NodeGroupId = nodeGroupId,
                ManagedNodeId = managedNodeId,
                Enabled = enabled,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.ApplicationAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return assignment.Id;
        }

        public async Task<Guid> SeedApplicationAsync(string name, string slug)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var application = new ApplicationDefinition
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = slug,
                Description = string.Empty,
                DefaultIntegrationMode = "runtime-api",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.Applications.Add(application);
            await dbContext.SaveChangesAsync();
            return application.Id;
        }

        public async Task<Guid> SeedNamespaceAsync(Guid applicationId, string name, string path)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var catalogNamespace = new NamespaceDefinition
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                Name = name,
                Path = path,
                Description = string.Empty,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.Namespaces.Add(catalogNamespace);
            await dbContext.SaveChangesAsync();
            return catalogNamespace.Id;
        }

        public async Task<Guid> SeedConfigItemAsync(Guid applicationId, Guid namespaceId, string key, string fullPath, bool isSecret, string valueType)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var configItem = new ConfigItemDefinition
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                NamespaceId = namespaceId,
                Key = key,
                FullPath = fullPath,
                ValueType = valueType,
                IsSecret = isSecret,
                IsRequired = false,
                DefaultRolloutPolicy = "immediate",
                ValidationSchemaJson = string.Empty,
                Description = string.Empty,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.ConfigItems.Add(configItem);
            await dbContext.SaveChangesAsync();
            return configItem.Id;
        }

        public async Task<Guid> SeedDraftValueAsync(
            Guid configItemId,
            ResourceScopeType scopeType,
            Guid scopeId,
            string valueJson,
            bool isSecret,
            string changeNote = "Seeded draft value")
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            var draftValueProtector = scope.ServiceProvider.GetRequiredService<IDraftValueProtector>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var draftValue = new DraftValue
            {
                Id = Guid.NewGuid(),
                ConfigItemId = configItemId,
                ScopeType = scopeType,
                ScopeId = scopeId,
                ValueJson = isSecret ? draftValueProtector.Protect(valueJson) : valueJson,
                IsSecret = isSecret,
                ChangeNote = changeNote,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.DraftValues.Add(draftValue);
            await dbContext.SaveChangesAsync();
            return draftValue.Id;
        }

        public async Task UpdateDraftValueAsync(
            Guid configItemId,
            ResourceScopeType scopeType,
            Guid scopeId,
            string valueJson,
            bool isSecret,
            string changeNote = "Updated seeded draft value")
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            var draftValueProtector = scope.ServiceProvider.GetRequiredService<IDraftValueProtector>();
            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            var draftValue = await dbContext.DraftValues.FirstAsync(
                x => x.ConfigItemId == configItemId && x.ScopeType == scopeType && x.ScopeId == scopeId);

            draftValue.ValueJson = isSecret ? draftValueProtector.Protect(valueJson) : valueJson;
            draftValue.IsSecret = isSecret;
            draftValue.ChangeNote = changeNote;
            draftValue.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync();
        }

        private async Task SeedUserAsync(IReadOnlyCollection<string> permissions)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<Argon2PasswordHasher>();

            await dbContext.Database.EnsureCreatedAsync();
            await EnsureInstallationAsync(dbContext);

            if (await dbContext.Users.AnyAsync(x => x.Username == "operator"))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var role = new RoleDefinition
            {
                Id = Guid.NewGuid(),
                Name = "TestCatalogOperator",
                Description = "Test-only catalog role.",
                IsSystem = false,
                CreatedAtUtc = now
            };

            foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
            {
                role.Permissions.Add(new RolePermission
                {
                    RoleDefinitionId = role.Id,
                    Permission = permission
                });
            }

            var user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Username = "operator",
                DisplayName = "Test Operator",
                PasswordHash = passwordHasher.Hash(TestPassword),
                Role = role.Name,
                IsEnabled = true,
                CreatedAtUtc = now
            };

            dbContext.RoleDefinitions.Add(role);
            dbContext.Users.Add(user);
            dbContext.RoleAssignments.Add(new RoleAssignment
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                RoleDefinitionId = role.Id,
                ScopeType = ResourceScopeType.Installation,
                ScopeId = Installation.SingletonId,
                CreatedByUserId = user.Id,
                CreatedAtUtc = now
            });

            await dbContext.SaveChangesAsync();
        }

        private static async Task EnsureInstallationAsync(SecretManagerDbContext dbContext)
        {
            if (await dbContext.Installations.IgnoreQueryFilters().AnyAsync())
            {
                return;
            }

            dbContext.Installations.Add(new Installation
            {
                Id = Installation.SingletonId,
                Name = "Test Installation",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                InitializedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }
}