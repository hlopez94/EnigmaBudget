SET @script_version = '0.3.0';
SELECT @ejecutado := enigma.fn_leveling_script_ejecutado(@script_version);
select @errmsg := CONCAT('SCRIPT ', @script_version, ' EJECUTADO EN FECHA ', @ejecutado);
DELIMITER $$
$$
IF @ejecutado
THEN 
	SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT = @errmsg;
END IF;
$$
DELIMITER ;



-- enigma.types_deposit_account definition
CREATE TABLE enigma.types_deposit_account (
	tda_id BIGINT auto_increment NOT NULL,
	tda_description varchar(128) NOT NULL,
	tda_name varchar(16) NOT NULL,
	tda_enum_name varchar(32) NOT NULL,
	tda_fecha_alta DATE NOT NULL,
	tda_fecha_modif DATE NOT NULL,
	tda_fecha_baja DATE NULL,
	CONSTRAINT PK_types_deposit_account PRIMARY KEY (tda_id)
);
commit;

ALTER TABLE enigma.types_deposit_account ADD CONSTRAINT UNI_types_deposit_account UNIQUE (tda_enum_name);

CREATE INDEX IDX_types_deposit_account_enum ON enigma.types_deposit_account (tda_enum_name);
CREATE INDEX IDX_types_deposit_account_baja ON enigma.types_deposit_account (tda_fecha_baja);


DELIMITER $$
$$
CREATE TRIGGER enigma.TG_TDA_INS
BEFORE INSERT 
	ON enigma.types_deposit_account FOR EACH ROW BEGIN 
   		SET new.tda_fecha_alta = SYSDATE(); 
		SET new.tda_fecha_modif = SYSDATE(); 
	END;$$
DELIMITER ;
	
DELIMITER $$
$$
CREATE TRIGGER enigma.TG_TDA_UPD
BEFORE UPDATE 
	ON enigma.types_deposit_account FOR EACH ROW BEGIN 
		IF old.tda_id <> new.tda_id THEN 
			SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT='No se puede cambiar ID';
		END IF;		
		SET new.tda_fecha_modif = SYSDATE(); 
	END;$$
DELIMITER ;

CREATE TABLE enigma.deposit_accounts (
	dea_id BIGINT NOT NULL auto_increment COMMENT 'ID Unico para cuenta depósito',
	dea_usu_id BIGINT NOT NULL COMMENT 'ID usuario duenio cuenta depósito',
    dea_tda_id BIGINT NOT NULL COMMENT  'Tipo cuenta depósito ID',
	dea_name varchar(100) NOT NULL COMMENT 'Nombre cuenta deposito',
	dea_description VARCHAR(100) NOT NULL COMMENT 'Nombre descriptivo cuenta depósito',
	dea_funds DECIMAL DEFAULT 0 NOT NULL COMMENT 'Fondos actuales',
    dea_country_code VARCHAR(3) NOT NULL COMMENT  'Codigo numerico ISO-4217 para moneda de cuenta depósito',
    dea_currency_code VARCHAR(3) NOT NULL COMMENT  'Codigo numerico ISO-4217 para pais de cuenta depósito',
    dea_fecha_alta DATETIME NOT NULL COMMENT  'Fecha de alta de nta deposito',
    dea_fecha_modif DATETIME NOT NULL COMMENT  'Fecha de modificación de cuenta deposito',
    dea_fecha_baja DATETIME NULL COMMENT  'Fecha de baja de cuenta depósito',
	CONSTRAINT PK_deposit_accounts PRIMARY KEY (dea_id),
	CONSTRAINT FK_depoist_accounts FOREIGN KEY (dea_usu_id) REFERENCES enigma.usuarios(usu_id),
	CONSTRAINT FK_deposit_accounts FOREIGN KEY (dea_tda_id) REFERENCES enigma.types_deposit_account(tda_id)
)
COMMENT='Tabla con datos de cuentas depósito';
COMMIT;

CREATE INDEX IDX_dea_baja ON enigma.deposit_accounts (dea_fecha_baja);

DELIMITER $$
$$
CREATE TRIGGER enigma.TG_DEA_INS
BEFORE INSERT 
	ON enigma.deposit_accounts FOR EACH ROW BEGIN 
   		SET new.dea_fecha_alta = SYSDATE(); 
		SET new.dea_fecha_modif = SYSDATE(); 
	END;$$
DELIMITER ;
	
DELIMITER $$
$$ BEGIN NOT ATOMIC
	SET @script_version = '0.3.0';
	SET @ejecutado := enigma.fn_leveling_script_ejecutado(@script_version);
	SET @errmsg := CONCAT('SCRIPT ', @script_version, ' EJECUTADO EN FECHA ', @ejecutado);

	IF @ejecutado
		THEN
			SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT = @errmsg;
		ELSE 
			CALL enigma.sp_leveling_script_ejecutado(@script_version);
			SELECT CONCAT('BASE DE DATOS NIVELADA A VERSION ' , @script_version ) estado_ejecucion;
	END IF;
	END$$
DELIMITER ;