-- DDL de referência da tabela do agregado Product.
-- O repositório PostgreSQL do Aedis mapeia as propriedades da entidade para colunas em snake_case
-- e detecta as colunas de auditoria/soft-delete pela presença delas. Aplique via sua ferramenta de
-- migração preferida (psql, Flyway, DbUp, etc.).

CREATE TABLE IF NOT EXISTS product (
    id           uuid PRIMARY KEY,
    code         varchar(50)  NOT NULL,
    name         varchar(200) NOT NULL,
    price        numeric(18,2) NOT NULL DEFAULT 0,
    created_at   timestamptz  NOT NULL DEFAULT now(),
    created_by   varchar(255),
    updated_at   timestamptz  NOT NULL DEFAULT now(),
    updated_by   varchar(255),
    updated_reason varchar(255),
    is_deleted   boolean      NOT NULL DEFAULT false,
    deleted_at   timestamptz,
    deleted_by   varchar(255)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_product_code
    ON product (code)
    WHERE is_deleted = false;
