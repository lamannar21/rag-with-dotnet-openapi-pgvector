CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE document_chunk (
    document_chunk_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    filename text NOT NULL,
    chunk_index integer NOT NULL,
    page_number integer NOT NULL,
    chunk_text text NOT NULL,
    embedding vector(1536) NOT NULL,
    embedding_model text NOT NULL,
    document_id uuid NULL
);

CREATE TABLE answer (
    answer_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    answer_text text NOT NULL
);

CREATE TABLE question (
    question_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    answer_id bigint NOT NULL REFERENCES answer(answer_id),
    question_text text NOT NULL,
    question_embedding vector(1536) NOT NULL,
    embedding_model text NOT NULL,
    document_id uuid NULL
);

CREATE INDEX document_chunk_document_id_idx ON document_chunk (document_id);
CREATE INDEX document_chunk_filename_idx ON document_chunk (filename);
CREATE INDEX question_document_id_idx ON question (document_id);

SELECT extversion FROM pg_extension WHERE extname = 'vector';