DELIMITER |
BEGIN NOT ATOMIC
	DECLARE VERSION_SCRIPT VARCHAR(16);
	DECLARE	NO_EJECUTADO boolean;

	SET VERSION_SCRIPT = '0.0.2';
	SET NO_EJECUTADO = NOT EXISTS(SELECT * FROM sql_leveling_info WHERE sli_script_version = '0.0.2');

	IF NO_EJECUTADO THEN
    
        CREATE TABLE usuarios (
        	usu_id binary(16) NOT NULL COMMENT 'ID Unico para el usuario',
        	usu_usuario varchar(100) NOT NULL COMMENT 'Nombre de usuario',
        	usu_correo varchar(100) NOT NULL COMMENT 'Correo asociado al Usuario',
        	usu_password varchar(256) NULL COMMENT 'Clave hasheada del usuario',
        	usu_seed varchar(32) NULL COMMENT 'Semilla de hasheo de la clave de usuario',
            usu_fecha_alta datetime NOT NULL COMMENT  'Fecha de alta del usuario',
            usu_fecha_modif datetime NOT NULL COMMENT  'Fecha de modificación del usuario',
            usu_fecha_baja datetime NULL COMMENT  'Fecha de baja del usuario',
        	CONSTRAINT PK_usuarios PRIMARY KEY (usu_id)
        ) COMMENT='Tabla con datos de usuarios';

        ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_correo UNIQUE (usu_correo);
        ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_usuario UNIQUE (usu_usuario);

        CREATE INDEX IDX_usuarios_usu_correo USING BTREE ON enigma.usuarios (usu_correo);

        CREATE INDEX IDX_usuarios_usu_usuario USING BTREE ON enigma.usuarios (usu_usuario)
        GO
                    CREATE TRIGGER enigma.TG_USUARIOS_INS
                    BEFORE INSERT 
                    	ON enigmausuarios FOR EACH ROW 
                        BEGIN 
                        	SET new.usu_id = (UNHEX(REPLACE(UUID(),'-','')));  
                       		SET new.usu_fecha_alta = SYSDATE(); 
                    		SET new.usu_fecha_modif = SYSDATE();        
                END
        GO
        
            DELIMITER $$
            CREATE TRIGGER TG_USUARIOS_UPD
            BEFORE UPDATE 
            	ON usuarios FOR EACH ROW 
                BEGIN 
            		IF old.usu_id <> new.usu_id THEN 
            			SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT='No se puede cambiar ID';
            		END IF;
    
            		SET new.usu_fecha_modif = SYSDATE(); 
            	END$$
            DELIMITER;

        INSERT INTO enigma.sql_leveling_info (sli_script_version, sli_script_fecha_ejecucion, sli_script_resultado) VALUES(VERSION_SCRIPT, SYSDATE(), 'OK');
	END IF;
    END|
    DELIMITER;