SET @script_version = '0.1.0';
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

CREATE TABLE enigma.usuarios (
	usu_id BIGINT AUTO_INCREMENT NOT NULL COMMENT 'ID Unico para el usuario',
	usu_usuario VARCHAR(100) NOT NULL COMMENT 'Nombre de usuario',
	usu_correo VARCHAR(100) NOT NULL COMMENT 'Correo asociado al Usuario',
	usu_password VARCHAR(256) NULL COMMENT 'Clave hasheada del usuario',
	usu_seed VARCHAR(64) NULL COMMENT 'Semilla de hasheo de la clave de usuario',
    usu_fecha_alta DATETIME NOT NULL COMMENT  'Fecha de alta del usuario',
    usu_fecha_modif DATETIME NOT NULL COMMENT  'Fecha de modificación del usuario',
    usu_fecha_baja DATETIME NULL COMMENT  'Fecha de baja del usuario',
	usu_correo_validado BOOL DEFAULT 0 NOT NULL COMMENT 'Indica si el mail brindado por el usuario fue validado',
	CONSTRAINT PK_usuarios PRIMARY KEY (usu_id)
)
COMMENT='Tabla con datos de usuarios';
commit;
ALTER TABLE enigma.usuarios ADD CONSTRAINT UNI_usuarios_usu_correo UNIQUE (usu_correo);
ALTER TABLE enigma.usuarios ADD CONSTRAINT UNI_usuarios_usu_usuario UNIQUE (usu_usuario);

CREATE INDEX IDX_usuarios_usu_correo ON enigma.usuarios (usu_correo);
CREATE INDEX IDX_usuarios_usu_usuario ON enigma.usuarios (usu_usuario);


DELIMITER $$
$$
CREATE TRIGGER enigma.TG_USUARIOS_INS
BEFORE INSERT 
	ON enigma.usuarios FOR EACH ROW BEGIN 
   		SET new.usu_fecha_alta = SYSDATE(); 
		SET new.usu_fecha_modif = SYSDATE(); 
	END;$$
DELIMITER ;
	
DELIMITER $$
$$
CREATE TRIGGER enigma.TG_USUARIOS_UPD
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
	usp_usu_id BIGINT NOT NULL,
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
CREATE TRIGGER enigma.TG_USUARIO_PERFIL_INS
BEFORE INSERT
ON usuario_perfil FOR EACH ROW
BEGIN 
	SET NEW.usp_fecha_alta = NOW();
	SET NEW.usp_fecha_modif = NOW();
END;$$
DELIMITER ;

DELIMITER $$
$$CREATE TRIGGER enigma.TG_USUARIO_PERFIL_UPD
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

CREATE TABLE enigma.usuarios_validacion_email (
	uve_usu_id BIGINT NOT NULL,
	uve_id INT auto_increment NOT NULL,
	uve_fecha_alta datetime NOT NULL,
	uve_fecha_baja datetime NOT NULL,
	uve_salt varchar(64) NOT NULL,
	uve_validado bool DEFAULT 0 NOT NULL,
	uve_nuevo_correo varchar(100) DEFAULT 0 NOT NULL,
	CONSTRAINT usuarios_validacion_email_PK PRIMARY KEY (uve_id),
	CONSTRAINT usuarios_validacion_email_FK FOREIGN KEY (uve_usu_id) REFERENCES enigma.usuarios(usu_id)
);
commit;
CREATE INDEX usuarios_validacion_email_uve_ID_IDX USING BTREE ON enigma.usuarios_validacion_email (uve_id);
CREATE INDEX usuarios_validacion_email_uve_salt_IDX USING BTREE ON enigma.usuarios_validacion_email (uve_salt,uve_id);

DELIMITER $$
$$ 
CREATE TRIGGER enigma.trg_uve_upd
BEFORE UPDATE
ON usuarios_validacion_email FOR EACH ROW
BEGIN
 	IF (new.uve_validado) THEN
		UPDATE enigma.usuarios usu
			SET usu.usu_correo = new.uve_nuevo_correo,
			usu.usu_correo_validado = TRUE 
		WHERE usu.usu_id = new.uve_usu_id;
	SET NEW.uve_fecha_baja = NOW();
	END IF;
END;
$$
DELIMITER ;

CALL enigma.sp_leveling_script_ejecutado(@script_version);
SELECT CONCAT('BASE DE DATOS NIVELADA A VERSION ' , @script_version ) estado_ejecucion;

