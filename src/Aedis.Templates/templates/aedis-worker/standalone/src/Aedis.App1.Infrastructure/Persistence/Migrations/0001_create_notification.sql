-- DDL de referência da tabela do agregado Notification.
-- O repositório PostgreSQL do Aedis mapeia as propriedades em snake_case e persiste enums como texto
-- maiúsculo. Aplique via sua ferramenta de migração preferida (psql, Flyway, DbUp, etc.).

CREATE TABLE IF NOT EXISTS notification (
    id           uuid PRIMARY KEY,
    code         varchar(100) NOT NULL,
    recipient    varchar(320) NOT NULL,
    content      text         NOT NULL DEFAULT '',
    status       varchar(20)  NOT NULL DEFAULT 'PENDING',
    created_at   timestamptz  NOT NULL DEFAULT now(),
    created_by   varchar(255),
    updated_at   timestamptz  NOT NULL DEFAULT now(),
    updated_by   varchar(255),
    updated_reason varchar(255),
    is_deleted   boolean      NOT NULL DEFAULT false,
    deleted_at   timestamptz,
    deleted_by   varchar(255)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_notification_code
    ON notification (code)
    WHERE is_deleted = false;
