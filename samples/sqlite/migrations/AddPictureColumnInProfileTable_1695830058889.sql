-- MIGRONDI:NAME=AddPictureColumnInProfileTable_1695830058889.sql
-- MIGRONDI:TIMESTAMP=1695830058889
-- ---------- MIGRONDI:UP ----------
alter table profiles add column profile_picture text;

-- ---------- MIGRONDI:DOWN ----------
alter table profiles drop column profile_picture;

