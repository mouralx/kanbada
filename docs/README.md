# Technical documentation

Read [the root README](../README.md) first to run Kanbada.

1. [Architecture and separation of concerns](architecture.md): system boundaries, source map, data flow, and design tradeoffs.
2. [Frontend](frontend.md): components, hooks, persistence, themes, translations, and extension points.
3. [API and Scalar](api.md): routes, requests, concurrency, errors, files, and interactive reference.
4. [Database](database.md): relational schema, JSON workspace contract, transactions, and migrations.
5. [Authentication and authorization](authentication.md): sessions, external providers, membership, sharing, and request security.
6. [Configuration and operations](operations.md): configuration, deployment, backups, diagnostics, and troubleshooting.
7. [Testing and contribution standards](testing.md): repeatable checks, meaningful coverage, and coding conventions.

Documentation describes the implementation in this repository. Source files and the generated OpenAPI document are the executable contract; update the relevant document whenever behavior changes.

[Large dataset and load testing](load-testing.md): repeatable EF Core reset/seed utility and dataset sizes.
