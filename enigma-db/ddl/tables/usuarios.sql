-- enigma.usuarios definition

CREATE TABLE `usuarios` (
  `usu_id` BIGINT auto_increment NOT NULL,
  `usu_usuario` varchar(100) NOT NULL COMMENT 'Nombre de usuario',
  `usu_correo` varchar(100) NOT NULL COMMENT 'Correo asociado al Usuario',
  `usu_password` varchar(256) DEFAULT NULL COMMENT 'Clave hasheada del usuario',
  `usu_seed` varchar(64) DEFAULT NULL COMMENT 'Semilla de hasheo de la clave de usuario',
  `usu_fecha_alta` datetime NOT NULL COMMENT 'Fecha de alta del usuario',
  `usu_fecha_modif` datetime NOT NULL COMMENT 'Fecha de modificaci├│n del usuario',
  `usu_fecha_baja` datetime DEFAULT NULL COMMENT 'Fecha de baja del usuario',
  `usu_correo_validado` tinyint(1) NOT NULL DEFAULT 0,
	CONSTRAINT PK_usuarios PRIMARY KEY (usu_id)
) COMMENT='Tabla con datos de usuarios';

ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_correo UNIQUE (usu_correo);
ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_usuario UNIQUE (usu_usuario);

CREATE INDEX IDX_usuarios_usu_correo ON enigma.usuarios (usu_correo);
CREATE INDEX IDX_usuarios_usu_usuario ON enigma.usuarios (usu_usuario);


DELIMITER $$
$$
CREATE TRIGGER TG_USUARIOS_INS
BEFORE INSERT 
	ON usuarios FOR EACH ROW BEGIN 
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