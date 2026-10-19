# RAG with .NET, OpenAI, and pgvector

A sample RAG application that answers questions about a specific product using its PDF manual. Users can ask about setup, operation, maintenance, or troubleshooting without searching the internet or browsing unrelated product information. Answers are grounded in the uploaded manual, making it easier to find information relevant to the exact product and model. The application uses OpenAI services over the internet, but does not perform web searches.

The React frontend uploads a PDF, the .NET API stores text and embeddings in PostgreSQL with pgvector, and an OpenAI agent answers questions using retrieved passages. Supported answers are cached by question similarity.

## Projects

| Directory | Purpose | Setup guide |
| --- | --- | --- |
| `SampleAPI` | ASP.NET Core 10 API, PDF indexing, background processing, and vector retrieval | [API readme](SampleAPI/readme.md) |
| `SampleUI` | React 19, TypeScript, and Vite frontend | [UI readme](SampleUI/README.md) |


## Prerequisites

- .NET 10 SDK (the runtime alone cannot build the API).
- Node.js 22.12+ on the 22.x line, or Node.js 24, with npm. These versions satisfy the checked-in frontend dependency requirements.
- PostgreSQL with pgvector. Docker is optional if you already have a suitable database.
- An OpenAI API key with access to `text-embedding-3-small` and `gpt-5-mini` (or other OpenAI model as defined in SampleAPI appsettings.json).
- A PDF containing selectable text for an end-to-end test.

## Postgres setup (with Docker)

### Create a Docker image and container.

Run the following command. Replace `<container-name>` and `<password>` with your chosen values before running the command.<br>This will create an image of the PostgreSQL database with the pgvector extension pre-installed and start a container for the image.


```powershell
docker run --name <container-name> `
  -e POSTGRES_USER=postgres `
  -e POSTGRES_PASSWORD=<password> `
  -p 5433:5432 `
  -v pgdata:/var/lib/postgresql `
  -d pgvector/pgvector:pg18-trixie
```

The response should indicate 'Download complete' and 'Downloaded new image for `pgvector/pgvector:pg18-trixie`'.

| Option | Description |
| --- | --- |
| `--name <container-name>` | Assigns a readable name to the container. |
| `-e POSTGRES_PASSWORD=<password>` | Sets the mandatory environment variable to the default user password. |
| `-p 5433:5432` | Maps port 5432 of the container to port 5433 on your local host system. |
| `-v pgdata:/var/lib/postgresql` | Creates a named Docker volume at the PostgreSQL 18+ mount location to persist your database data. |
| `-d pgvector/pgvector:pg18-trixie` | Runs PostgreSQL 18 with pgvector installed in detached mode. |

You can check the container in Docker Desktop or by running the following commands.

```powershell
# list images
docker images

# list running containers
docker ps

# list all containers
docker ps --all
```
### Set environment variables

```powershell
$env:OPENAI_API_KEY = '<your-api-key>'
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5433;Database=<database-name>;Username=postgres;Password=<password>'
```

### Create a database in postgres
With the postgres container <container-name> running, execute the following command. 

```powershell
docker exec -it <container-name> createdb -U postgres <database-name>
# Verify - list the current databases in postgres
docker exec -it <container-name> psql -U postgres -l
```

### Create the database tables
Run [dbsetup.sql](SampleAPI/dbsetup.sql) once in a **new, empty database**. It enables the vector extension, creates the `document_chunk`, `answer`, and `question` tables and their indexes, and reports the installed vector extension version. The API does not run this script automatically, and the script is not a migration for an existing schema.

From the `SampleAPI` directory, copy the script into the running Docker container and execute it:

```powershell
# copy the dbsetup file to the container
docker cp SampleAPI/dbsetup.sql <container-name>:/tmp/dbsetup.sql

# run the dbsetup file in the container
docker exec <container-name> psql -U postgres -d <database-name> -v ON_ERROR_STOP=1 -f /tmp/dbsetup.sql
```

For an existing PostgreSQL server, run the local script with `psql` from the `SampleAPI` folder, adjusting the connection options to match your server:

```powershell
psql -h localhost -p 5433 -U postgres -d <database-name> -v ON_ERROR_STOP=1 -f ./dbsetup.sql
```
### Verify the database connection.

Open the psql cli:

```powershell
docker exec -it <container-name> psql -h localhost -U <user-name> -d <database-name> -W
```
in the psql cli run the following query
```psql
SELECT current_database(), current_user;
```

You should see something like:
| current_database | current_user |
|------------------|--------------|
| pg-vector-db     | postgres     |

(1 row)

Next run the following
```psql
\dt
```
this should list the following tables.
### List of tables

| Schema | Name           | Type  | Owner    |
|--------|----------------|-------|----------|
| public | answer         | table | postgres |
| public | document_chunk | table | postgres |
| public | question       | table | postgres |

(3 rows)

## License

MIT [See License](LICENSE)

[SampleAPI readme →](SampleAPI/readme.md)
