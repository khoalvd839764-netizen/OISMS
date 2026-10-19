#!/usr/bin/env sh
# ==============================================================================
# OISMS - Docker PostgreSQL Initialization Script
# Mount location: /docker-entrypoint-initdb.d/01-init-oisms.sh
#
# Key Requirements:
# 1. Runs automatically on empty volume initialization.
# 2. Preserves existing data; does not DROP database or tables.
# 3. Resolves CREATE DATABASE / \connect conflict with Docker's POSTGRES_DB.
# 4. Provisions least-privilege backend runtime login role without SQL interpolation.
# 5. Enforces security validations (no direct or indirect admin privilege, no schema/table ownership).
# 6. Does not print passwords to log and supports quotes, semicolons, $, and special characters.
# 7. Validates correct trigger 'ledger_append_only' on public.inventory_transactions.
# 8. Verifies all 29 required tables explicitly; fails fast on incomplete schema.
# ==============================================================================
set -e

# 1. Validate environment configuration
TARGET_DB="${POSTGRES_DB:-oisms}"
SUPERUSER="${POSTGRES_USER:-postgres}"
APP_USER="${OISMS_APP_USER}"
APP_PASSWORD="${OISMS_APP_PASSWORD}"

if [ -z "$APP_USER" ]; then
    echo ">> [ERROR] OISMS_APP_USER is not set. Please provide a valid runtime username via environment variable." >&2
    exit 1
fi

if [ -z "$APP_PASSWORD" ]; then
    echo ">> [ERROR] OISMS_APP_PASSWORD is not set. Please provide a valid runtime password via environment variable." >&2
    exit 1
fi

# Reject runtime user if identical to admin or group role
if [ "$APP_USER" = "$SUPERUSER" ] || [ "$APP_USER" = "postgres" ] || [ "$APP_USER" = "oisms_runtime" ]; then
    echo ">> [ERROR] Runtime username '$APP_USER' cannot be the superuser ('$SUPERUSER'/'postgres') or group role ('oisms_runtime')!" >&2
    exit 1
fi

echo "=========================================================="
echo ">> [OISMS Init] PostgreSQL Initialization"
echo ">> Target Database: $TARGET_DB"
echo ">> Admin User:      $SUPERUSER"
echo ">> Runtime Group:   oisms_runtime (NOLOGIN)"
echo ">> Runtime Login:   $APP_USER"
echo "=========================================================="

# 2. Locate schema file
SCHEMA_FILE=""
if [ -f "/docker/postgres/oisms_schema.sql" ]; then
    SCHEMA_FILE="/docker/postgres/oisms_schema.sql"
elif [ -f "/docker-entrypoint-initdb.d/oisms_schema.sql" ]; then
    SCHEMA_FILE="/docker-entrypoint-initdb.d/oisms_schema.sql"
elif [ -f "/docs/database/oisms_schema.sql" ]; then
    SCHEMA_FILE="/docs/database/oisms_schema.sql"
else
    echo ">> [ERROR] Schema file oisms_schema.sql not found!" >&2
    exit 1
fi

# 3. Determine database and table status
DB_EXISTS=$(psql -v ON_ERROR_STOP=1 --username "$SUPERUSER" --dbname "postgres" -tAc "SELECT 1 FROM pg_database WHERE datname='$TARGET_DB'")

if [ "$DB_EXISTS" != "1" ]; then
    echo ">> Database '$TARGET_DB' does not exist. Creating and executing $SCHEMA_FILE directly..."
    psql -v ON_ERROR_STOP=1 --username "$SUPERUSER" --dbname "postgres" -f "$SCHEMA_FILE"
else
    echo ">> Database '$TARGET_DB' already exists (pre-created by Docker entrypoint or existing volume)."
    TABLE_COUNT=$(psql -v ON_ERROR_STOP=1 --username "$SUPERUSER" --dbname "$TARGET_DB" -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'")

    if [ "$TABLE_COUNT" -ge 29 ]; then
        echo ">> Database '$TARGET_DB' already contains $TABLE_COUNT base tables. Schema already exists; preserving existing data, skipping DDL."
    elif [ "$TABLE_COUNT" -gt 0 ]; then
        echo ">> [FATAL ERROR] Database '$TARGET_DB' contains $TABLE_COUNT base tables (expected 29). Incomplete schema detected on existing data volume!" >&2
        echo ">> [FATAL ERROR] Refusing to overwrite or drop existing data. Incremental database migration required!" >&2
        exit 1
    else
        echo ">> Database '$TARGET_DB' exists but is empty. Applying schema without duplicate CREATE DATABASE..."
        # Extract and apply only the schema DDL starting from -- OISMS_SCHEMA_BEGIN
        sed -n '/-- OISMS_SCHEMA_BEGIN/,$p' "$SCHEMA_FILE" | psql -v ON_ERROR_STOP=1 --username "$SUPERUSER" --dbname "$TARGET_DB"
        echo ">> Schema successfully applied to '$TARGET_DB'."
    fi
fi

# 4. Export variables for psql \getenv to safely pass parameters through a temporary table without SQL interpolation or dollar-quote issues
export OISMS_APP_USER
export OISMS_APP_PASSWORD
export POSTGRES_DB="$TARGET_DB"
export POSTGRES_USER="$SUPERUSER"

echo ">> Configuring and validating runtime user '$APP_USER'..."

psql -v ON_ERROR_STOP=1 --username "$SUPERUSER" --dbname "$TARGET_DB" <<-'EOSQL'
\set ON_ERROR_STOP on
\getenv app_user OISMS_APP_USER
\getenv app_password OISMS_APP_PASSWORD
\getenv target_db POSTGRES_DB
\getenv admin_user POSTGRES_USER

DROP TABLE IF EXISTS _oisms_init_params;
CREATE TEMPORARY TABLE _oisms_init_params (
    app_user text NOT NULL,
    app_password text NOT NULL,
    target_db text NOT NULL,
    admin_user text NOT NULL
);

INSERT INTO _oisms_init_params (app_user, app_password, target_db, admin_user)
VALUES (:'app_user', :'app_password', :'target_db', :'admin_user');

DO $do$
DECLARE
    v_user text;
    v_pass text;
    v_db text;
    v_admin text;
    v_is_super boolean;
    v_createdb boolean;
    v_createrole boolean;
    v_replication boolean;
    v_bypassrls boolean;
    v_has_admin_role boolean;
    v_owns_db boolean;
    v_owns_schema boolean;
    v_owns_table boolean;
BEGIN
    SELECT app_user, app_password, target_db, admin_user
    INTO STRICT v_user, v_pass, v_db, v_admin
    FROM _oisms_init_params;

    DROP TABLE _oisms_init_params;

    IF v_user IS NULL OR btrim(v_user) = '' THEN
        RAISE EXCEPTION 'Runtime username cannot be empty.';
    END IF;

    IF v_pass IS NULL OR v_pass = '' THEN
        RAISE EXCEPTION 'Runtime password cannot be empty.';
    END IF;

    IF v_user = v_admin OR v_user = 'postgres' OR v_user = 'oisms_runtime' THEN
        RAISE EXCEPTION 'Runtime user "%" cannot be identical to admin ("%") or group role ("oisms_runtime").', v_user, v_admin;
    END IF;

    -- If role exists, perform rigorous security check for direct and inherited privileges
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_user) THEN
        SELECT rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls
        INTO v_is_super, v_createdb, v_createrole, v_replication, v_bypassrls
        FROM pg_roles WHERE rolname = v_user;

        IF v_is_super OR v_createdb OR v_createrole OR v_replication OR v_bypassrls THEN
            RAISE EXCEPTION 'Security violation: Existing role "%" has direct administrative privileges (rolsuper=%, rolcreatedb=%, rolcreaterole=%, rolreplication=%, rolbypassrls=%). Unsafe for backend runtime! Refusing to modify.',
                v_user, v_is_super, v_createdb, v_createrole, v_replication, v_bypassrls;
        END IF;

        -- Check indirect / inherited membership in superuser, createdb, createrole, replication, bypassrls, or privileged roles
        SELECT EXISTS (
            SELECT 1 FROM pg_roles r
            WHERE (r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls OR r.rolname IN (
                'pg_read_all_data', 'pg_write_all_data', 'pg_database_owner',
                'pg_execute_server_program', 'pg_read_server_files', 'pg_write_server_files'
            ))
            AND pg_has_role(v_user, r.oid, 'member')
        ) INTO v_has_admin_role;

        IF v_has_admin_role THEN
            RAISE EXCEPTION 'Security violation: Existing role "%" directly or indirectly inherits administrative privileges (SUPERUSER, CREATEDB, CREATEROLE, REPLICATION, BYPASSRLS or privileged server roles). Unsafe for backend runtime! Refusing to modify.', v_user;
        END IF;

        -- Check effective ownership of target database
        SELECT EXISTS (
            SELECT 1 FROM pg_database d
            WHERE d.datname = v_db
              AND (d.datdba = (SELECT oid FROM pg_roles WHERE rolname = v_user)
                   OR pg_has_role(v_user, d.datdba, 'member'))
        ) INTO v_owns_db;

        IF v_owns_db THEN
            RAISE EXCEPTION 'Security violation: Existing role "%" directly or indirectly owns database "%". Backend runtime account must not own the database! Refusing to modify.', v_user, v_db;
        END IF;

        -- Check effective ownership of schema public
        SELECT EXISTS (
            SELECT 1 FROM pg_namespace n
            WHERE n.nspname = 'public'
              AND (n.nspowner = (SELECT oid FROM pg_roles WHERE rolname = v_user)
                   OR pg_has_role(v_user, n.nspowner, 'member'))
        ) INTO v_owns_schema;

        IF v_owns_schema THEN
            RAISE EXCEPTION 'Security violation: Existing role "%" directly or indirectly owns schema "public". Backend runtime account must not own schema! Refusing to modify.', v_user;
        END IF;

        -- Check effective ownership of any table in schema public
        SELECT EXISTS (
            SELECT 1 FROM pg_class c
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = 'public' AND c.relkind = 'r'
              AND (c.relowner = (SELECT oid FROM pg_roles WHERE rolname = v_user)
                   OR pg_has_role(v_user, c.relowner, 'member'))
        ) INTO v_owns_table;

        IF v_owns_table THEN
            RAISE EXCEPTION 'Security violation: Existing role "%" directly or indirectly owns tables in schema "public". Backend runtime account must not own tables! Refusing to modify.', v_user;
        END IF;

        -- Existing role is safe: update password securely using format %L
        EXECUTE format('ALTER ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS', v_user, v_pass);
        RAISE NOTICE 'Existing runtime role "%" verified safe and password updated.', v_user;
    ELSE
        -- Create new role securely with format %I and %L
        EXECUTE format('CREATE ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS', v_user, v_pass);
        RAISE NOTICE 'Runtime role "%" created successfully.', v_user;
    END IF;

    -- Grant access
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', v_db, v_user);
    EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', v_user);
    EXECUTE format('GRANT oisms_runtime TO %I', v_user);
    EXECUTE format('ALTER ROLE %I SET search_path = public, pg_catalog', v_user);
END
$do$;
EOSQL

FINAL_TABLES=$(psql -v ON_ERROR_STOP=1 --username "$SUPERUSER" --dbname "$TARGET_DB" -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'")
TRIGGER_COUNT=$(psql -v ON_ERROR_STOP=1 --username "$SUPERUSER" --dbname "$TARGET_DB" -tAc "
    SELECT count(*) FROM pg_trigger tg
    JOIN pg_class c ON tg.tgrelid = c.oid
    JOIN pg_namespace n ON c.relnamespace = n.oid
    WHERE n.nspname = 'public'
      AND c.relname = 'inventory_transactions'
      AND tg.tgname = 'ledger_append_only'
      AND tg.tgenabled IN ('O', 'A')
      AND NOT tg.tgisinternal
")

MISSING_TABLES=$(psql -v ON_ERROR_STOP=1 --username "$SUPERUSER" --dbname "$TARGET_DB" -tAc "
    WITH required(t) AS (
        VALUES
        ('tenants'),('users'),('refresh_tokens'),('branches'),('warehouses'),
        ('categories'),('brands'),('products'),('skus'),('suppliers'),
        ('goods_receipts'),('goods_receipt_items'),('inventory_balances'),
        ('warehouse_inventory_balances'),('stock_transfers'),('stock_transfer_items'),
        ('stock_counts'),('stock_count_items'),('inventory_adjustments'),
        ('inventory_adjustment_items'),('orders'),('order_items'),('reservations'),
        ('payments'),('sales_returns'),('sales_return_items'),('inventory_transactions'),
        ('daily_sales_summaries'),('replenishment_suggestions')
    )
    SELECT string_agg(r.t, ', ')
    FROM required r
    LEFT JOIN information_schema.tables t
      ON t.table_schema = 'public' AND t.table_name = r.t AND t.table_type = 'BASE TABLE'
    WHERE t.table_name IS NULL;
")

if [ -n "$MISSING_TABLES" ]; then
    echo "==========================================================" >&2
    echo ">> [FATAL ERROR] Initialization verification failed: Schema is missing required tables!" >&2
    echo ">> Missing tables: $MISSING_TABLES" >&2
    echo "==========================================================" >&2
    exit 1
fi

if [ "$FINAL_TABLES" -ne 29 ] || [ "$TRIGGER_COUNT" -ne 1 ]; then
    echo "==========================================================" >&2
    echo ">> [FATAL ERROR] Initialization verification failed!" >&2
    echo ">> Expected 29 base tables, found: $FINAL_TABLES" >&2
    echo ">> Expected 1 active ledger_append_only trigger, found: $TRIGGER_COUNT" >&2
    echo "==========================================================" >&2
    exit 1
fi

echo "=========================================================="
echo ">> [OISMS Init] Database Initialization Complete"
echo ">> Total public base tables:                 $FINAL_TABLES (all 29 verified)"
echo ">> Ledger trigger (ledger_append_only):      $TRIGGER_COUNT (active and enabled)"
echo ">> Runtime login user:                       $APP_USER (granted oisms_runtime)"
echo "=========================================================="
