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