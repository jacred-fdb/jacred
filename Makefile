# JacRed — primary build interface. See also scripts/build.sh.
#
# Usage:
#   make help
#   make publish
#   make publish RID=linux-arm64
#   make publish RID="linux-x64 linux-arm64"
#   make publish-linux-arm64
#   make publish-all

.DEFAULT_GOAL := help

DOTNET       ?= dotnet
CONFIGURATION ?= Debug
DOCKER_IMAGE ?= jacred
# Optional; empty = current host RID (scripts/build.sh default)
RID          ?=

PROJECT      := JacRed.csproj
TEST_PROJECT := tests/JacRed.Tests/JacRed.Tests.csproj
BUILD_SCRIPT := ./scripts/build.sh
WEB_SCRIPT   := ./scripts/build-web-ui.sh
VERSION_SCRIPT := ./scripts/generate-version.sh

.PHONY: help restore web build publish dist publish-all run dev-web \
	test test-web test-e2e lint-web gen-api version docker clean clean-all

help: ## Show this help
	@printf 'JacRed build targets\n\n'
	@printf 'Usage:\n'
	@printf '  make <target> [RID=...] [VARIABLE=value]\n\n'
	@printf 'Targets:\n'
	@awk 'BEGIN {FS = ":.*## "}; /^[a-zA-Z0-9_.-]+:.*?## / {printf "  %-16s %s\n", $$1, $$2}' $(MAKEFILE_LIST)
	@printf '\nPublish examples:\n'
	@printf '  make publish\n'
	@printf '  make publish RID=linux-arm64\n'
	@printf '  make publish RID="linux-x64 osx-arm64"\n'
	@printf '  make publish-linux-arm64\n'
	@printf '  make publish-all\n'

restore: ## Restore .NET packages
	$(DOTNET) restore $(PROJECT)

web: ## Build Vue SPA into wwwroot/
	$(WEB_SCRIPT)

build: ## Debug-build .NET project (SPA not required)
	$(DOTNET) build $(PROJECT) --configuration $(CONFIGURATION)

publish: ## Publish self-contained build (RID= optional)
	$(BUILD_SCRIPT) $(RID)

dist: publish ## Alias for publish

publish-all: ## Publish for all supported RIDs
	$(BUILD_SCRIPT) --all

publish-%: ## Publish for a specific RID (e.g. make publish-linux-arm64)
	$(BUILD_SCRIPT) $*

run: ## Run the ASP.NET app
	$(DOTNET) run --project $(PROJECT)

dev-web: ## Start Vite dev server (web/)
	cd web && npm run dev

test: ## Run .NET tests
	$(DOTNET) test $(TEST_PROJECT)

test-web: ## Run web unit tests
	cd web && npm test

test-e2e: ## Run web Playwright e2e tests
	cd web && npm run test:e2e

lint-web: ## Lint and typecheck web/
	cd web && npm run lint && npm run typecheck

gen-api: ## Regenerate web OpenAPI TypeScript types
	cd web && npm run gen:api

version: ## Smoke-generate VersionInfo.g.cs into obj/
	@mkdir -p obj
	$(VERSION_SCRIPT) obj/VersionInfo.g.cs
	@echo "Wrote obj/VersionInfo.g.cs"

docker: ## Build Docker image ($(DOCKER_IMAGE))
	docker build -t $(DOCKER_IMAGE) .

clean: ## Remove build artifacts (bin, obj, wwwroot, dist, …)
	rm -rf bin obj wwwroot web/dist .builds dist
	rm -rf tests/JacRed.Tests/bin tests/JacRed.Tests/obj
	@echo "Cleaned build outputs"

clean-all: clean ## clean + remove web/node_modules
	rm -rf web/node_modules
	@echo "Removed web/node_modules"
