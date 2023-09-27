-- MIGRONDI:NAME=CreateUsersTable_1695829818636.sql
-- MIGRONDI:TIMESTAMP=1695829818636
-- ---------- MIGRONDI:UP ----------
create table users (
    id integer primary key,
    name text not null,
    email text not null,
    password text not null,
    created_at datetime not null,
    updated_at datetime not null
);

-- ---------- MIGRONDI:DOWN ----------
drop table users;


