CREATE TABLE enigma.paises (
	pai_iso2 varchar(2) NOT NULL,
	pai_iso3 varchar(3) NOT NULL,
	pai_nombre varchar(100) NOT NULL,
	pai_nombre_int varchar(100) NOT NULL,
	pai_phone_code int NOT NULL,
	CONSTRAINT paises_PK PRIMARY KEY (pai_iso2)
);

