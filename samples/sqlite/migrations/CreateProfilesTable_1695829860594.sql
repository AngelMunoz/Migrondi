-- MIGRONDI:NAME=CreateProfilesTable_1695829860594.sql
-- MIGRONDI:TIMESTAMP=1695829860594
-- ---------- MIGRONDI:UP ----------
create table profiles (
    id integer primary key,
    user_id integer not null,
    bio text,
    created_at datetime not null,
    updated_at datetime not null,
    foreign key (user_id) references users (id)
);

-- ---------- MIGRONDI:DOWN ----------
drop table profiles;


