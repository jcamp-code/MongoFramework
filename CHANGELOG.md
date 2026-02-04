# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.0.2] - 2026-02-04

### Fixed

- `MaxAsync` and `MinAsync` with nullable selectors now return `null` for empty collections instead of throwing `InvalidOperationException`, matching EF Core behavior
  - `MaxAsync(e => (int?)e.Value)` returns `null` when collection is empty
  - `MinAsync(e => (int?)e.Value)` returns `null` when collection is empty
  - Non-nullable selectors continue to throw `InvalidOperationException` for empty collections

## [3.0.1] - 2026-02-03

### Added

- `ToDictionaryAsync` extension methods for `IQueryable<T>` with all standard overloads:
  - `ToDictionaryAsync(keySelector)`
  - `ToDictionaryAsync(keySelector, comparer)`
  - `ToDictionaryAsync(keySelector, elementSelector)`
  - `ToDictionaryAsync(keySelector, elementSelector, comparer)`
