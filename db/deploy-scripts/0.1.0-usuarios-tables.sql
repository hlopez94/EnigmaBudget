SET @script_version = '0.0.2';
SELECT @ejecutado := enigma.fn_leveling_script_ejecutado(@script_version);

DELIMITER $$
$$
IF @ejecutado is not null
THEN 
	SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT = 'SCRIPT 0.0.2 YA HA SIDO EJECUTADO';
END IF;
$$
DELIMITER ;

CREATE TABLE enigma.usuarios (
	usu_id BINARY(16) NOT NULL COMMENT 'ID Unico para el usuario',
	usu_usuario varchar(100) NOT NULL COMMENT 'Nombre de usuario',
	usu_correo varchar(100) NOT NULL COMMENT 'Correo asociado al Usuario',
	usu_password varchar(256) NULL COMMENT 'Clave hasheada del usuario',
	usu_seed varchar(64) NULL COMMENT 'Semilla de hasheo de la clave de usuario',
    usu_fecha_alta datetime NOT NULL COMMENT  'Fecha de alta del usuario',
    usu_fecha_modif datetime NOT NULL COMMENT  'Fecha de modificación del usuario',
    usu_fecha_baja datetime NULL COMMENT  'Fecha de baja del usuario',
	CONSTRAINT PK_usuarios PRIMARY KEY (usu_id)
)
COMMENT='Tabla con datos de usuarios';
commit;
ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_correo UNIQUE (usu_correo);
ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_usuario UNIQUE (usu_usuario);

CREATE INDEX IDX_usuarios_usu_correo ON enigma.usuarios (usu_correo);
CREATE INDEX IDX_usuarios_usu_usuario ON enigma.usuarios (usu_usuario);


DELIMITER $$
$$
CREATE TRIGGER TG_USUARIOS_INS
BEFORE INSERT 
	ON usuarios FOR EACH ROW BEGIN 
    	SET new.usu_id = (UNHEX(REPLACE(UUID(),'-','')));  
   		SET new.usu_fecha_alta = SYSDATE(); 
		SET new.usu_fecha_modif = SYSDATE(); 
	END;$$
DELIMITER ;
	
DELIMITER $$
$$
CREATE TRIGGER TG_USUARIOS_UPD
BEFORE UPDATE 
	ON usuarios FOR EACH ROW BEGIN 
		IF old.usu_id <> new.usu_id THEN 
			SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT='No se puede cambiar ID';
		END IF;		
		SET new.usu_fecha_modif = SYSDATE(); 
	END;$$
DELIMITER ;

-- enigma.usuario_perfil definition
CREATE TABLE enigma.usuario_perfil (
	usp_usu_id binary(16) NOT NULL,
	usp_nombre varchar(100) NULL,
	usp_fecha_nacimiento DATE NULL,
	usp_tel_cod_pais SMALLINT NULL,
	usp_tel_cod_area SMALLINT NULL,
	usp_tel_nro INT NULL,
	usp_fecha_alta datetime NOT NULL,
	usp_fecha_modif datetime NOT NULL,
	usp_fecha_baja datetime NULL,
	CONSTRAINT PK_usuario_perfil PRIMARY KEY (usp_usu_id),
	CONSTRAINT FK_usuario_perfil_usuarios FOREIGN KEY (usp_usu_id) REFERENCES enigma.usuarios(usu_id),
	CONSTRAINT CHK_usuario_perfil_tel CHECK (
					(
						usp_tel_cod_pais IS NOT NULL AND 
                    	usp_tel_cod_area IS NOT NULL AND
                    	usp_tel_nro IS NOT NULL 
                    ) 
                    OR
                    (
                    	usp_tel_cod_pais IS NULL AND 
                    	usp_tel_cod_area IS NULL AND
                    	usp_tel_nro IS NULL 
                    )
                    )
);

DELIMITER $$
$$
CREATE TRIGGER TG_USUARIO_PERFIL_INS
BEFORE INSERT
ON usuario_perfil FOR EACH ROW
BEGIN 
	SET NEW.usp_fecha_alta = NOW();
	SET NEW.usp_fecha_modif = NOW();
END;$$
DELIMITER ;

DELIMITER $$
$$CREATE TRIGGER TG_USUARIO_PERFIL_UPD
BEFORE UPDATE
ON usuario_perfil FOR EACH ROW
BEGIN 
	IF old.usp_usu_id <> new.usp_usu_id THEN 
			SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT='No se puede cambiar ID';
		END IF;

	SET NEW.usp_fecha_modif = NOW();
END;
$$
DELIMITER ;

CALL enigma.sp_leveling_script_ejecutado(@script_version);
SELECT CONCAT('BASE DE DATOS NIVELADA A VERSION ', @script_version);