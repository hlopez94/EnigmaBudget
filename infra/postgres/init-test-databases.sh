#!/usr/bin/env sh
set -eu

createdb --username "$POSTGRES_USER" --owner "$POSTGRES_USER" enigmabudget_test
