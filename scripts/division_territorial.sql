/* ============================================================================
   DIVISION TERRITORIAL GENERICA + DIRECCIONES POR PAIS - re-ejecutable
   Base: CoreBancario_<codigo>  (seleccione su base antes de ejecutar; sin USE)

   Requisitos previos: listas_valores_admin.sql e interlocutor_multiples.sql.

   Modelo:
     - La direccion de un interlocutor tiene su PROPIO pais (lista de valores PAIS),
       independiente del pais regulador: un cliente puede tener una direccion en CR
       y otra en USA.
     - Cada pais define hasta 4 niveles (nombre, modo Lista/Texto, obligatorio) y un
       codigo postal opcional con patron de validacion:
           CR : Provincia, Canton, Distrito (listas dependientes) + Barrio (texto)
           USA: Estado (lista) + Ciudad (texto); ZIP obligatorio ^\d{5}(-\d{4})?$
       Un pais sin configuracion usa 4 niveles de texto libre.
     - Un nivel en modo Lista guarda el id de gen.DIVISION_TERRITORIAL; en modo
       Texto guarda el texto digitado.

   Datos de Costa Rica: provienen del paquete publico MIT @juniorov/cr-locations
   (82 cantones y 482 distritos; la lista oficial tiene 84 y 488 y algunos
   nombres de distrito vienen sin tilde). Es una BASE DE PARTIDA: complete o
   corrija con la lista oficial desde Administracion > Division territorial
   (carga CSV por codigo: el codigo existente se actualiza, el nuevo se crea).
   ============================================================================ */
SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   1. Lista de valores PAIS (editable desde Listas de Valores)
   --------------------------------------------------------------------------- */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT N'PAIS', N'Pais', N'Paises para direcciones. La estructura de la direccion de cada pais se define en Division territorial.', 0, N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'PAIS');

DECLARE @paises TABLE (codigo nvarchar(10), nombre nvarchar(100), orden int);
INSERT INTO @paises VALUES
 (N'CR', N'Costa Rica', 1), (N'US', N'Estados Unidos', 2), (N'PA', N'Panama', 3), (N'NI', N'Nicaragua', 4),
 (N'HN', N'Honduras', 5), (N'SV', N'El Salvador', 6), (N'GT', N'Guatemala', 7), (N'MX', N'Mexico', 8),
 (N'CO', N'Colombia', 9), (N'ES', N'Espana', 10), (N'CA', N'Canada', 11);

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, p.codigo, p.nombre, p.orden, 0, N'sistema'
FROM @paises p
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'PAIS'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = p.codigo);
GO

/* ---------------------------------------------------------------------------
   2. Configuracion de direccion por pais, y division territorial
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'gen.PAIS_DIRECCION_CONFIG', N'U') IS NULL
BEGIN
    CREATE TABLE gen.PAIS_DIRECCION_CONFIG (
        id_pais                    bigint        NOT NULL,   -- item de la lista PAIS
        etiqueta_codigo_postal     nvarchar(50)  NULL,       -- NULL = el pais no usa codigo postal
        patron_codigo_postal       nvarchar(200) NULL,       -- expresion regular; NULL = sin validacion de formato
        codigo_postal_obligatorio  bit           NOT NULL CONSTRAINT DF_PAIS_DIRECCION_CONFIG_cp_obl DEFAULT (0),
        creado_por                 nvarchar(100) NOT NULL,
        creado_en                  datetime2     NOT NULL CONSTRAINT DF_PAIS_DIRECCION_CONFIG_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por             nvarchar(100) NULL,
        modificado_en              datetime2     NULL,
        CONSTRAINT PK_PAIS_DIRECCION_CONFIG PRIMARY KEY (id_pais),
        CONSTRAINT FK_PAIS_DIRECCION_CONFIG_PAIS FOREIGN KEY (id_pais) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

IF OBJECT_ID(N'gen.PAIS_DIRECCION_NIVEL', N'U') IS NULL
BEGIN
    CREATE TABLE gen.PAIS_DIRECCION_NIVEL (
        id_pais        bigint        NOT NULL,
        nivel          tinyint       NOT NULL,
        nombre         nvarchar(50)  NOT NULL,       -- Provincia, Canton, Distrito, Barrio, Estado, Ciudad...
        modo           nvarchar(10)  NOT NULL,       -- Lista (gen.DIVISION_TERRITORIAL) o Texto libre
        es_obligatorio bit           NOT NULL CONSTRAINT DF_PAIS_DIRECCION_NIVEL_obl DEFAULT (0),
        creado_por     nvarchar(100) NOT NULL,
        creado_en      datetime2     NOT NULL CONSTRAINT DF_PAIS_DIRECCION_NIVEL_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por nvarchar(100) NULL,
        modificado_en  datetime2     NULL,
        CONSTRAINT PK_PAIS_DIRECCION_NIVEL PRIMARY KEY (id_pais, nivel),
        CONSTRAINT FK_PAIS_DIRECCION_NIVEL_CONFIG FOREIGN KEY (id_pais) REFERENCES gen.PAIS_DIRECCION_CONFIG (id_pais),
        CONSTRAINT CK_PAIS_DIRECCION_NIVEL_nivel CHECK (nivel BETWEEN 1 AND 4),
        CONSTRAINT CK_PAIS_DIRECCION_NIVEL_modo CHECK (modo IN ('Lista','Texto'))
    );
END
GO

IF OBJECT_ID(N'gen.DIVISION_TERRITORIAL', N'U') IS NULL
BEGIN
    CREATE TABLE gen.DIVISION_TERRITORIAL (
        id_division        bigint IDENTITY(1,1) NOT NULL,
        id_pais            bigint        NOT NULL,
        nivel              tinyint       NOT NULL,
        id_division_padre  bigint        NULL,          -- NULL en el nivel 1
        codigo             nvarchar(20)  NOT NULL,      -- unico por pais (CR: INEC 1 / 101 / 10101; USA: USPS)
        nombre             nvarchar(150) NOT NULL,
        estado             nvarchar(20)  NOT NULL CONSTRAINT DF_DIVISION_TERRITORIAL_estado DEFAULT ('Activo'),
        creado_por         nvarchar(100) NOT NULL,
        creado_en          datetime2     NOT NULL CONSTRAINT DF_DIVISION_TERRITORIAL_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por     nvarchar(100) NULL,
        modificado_en      datetime2     NULL,
        CONSTRAINT PK_DIVISION_TERRITORIAL PRIMARY KEY (id_division),
        CONSTRAINT FK_DIVISION_TERRITORIAL_PAIS FOREIGN KEY (id_pais) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_DIVISION_TERRITORIAL_PADRE FOREIGN KEY (id_division_padre) REFERENCES gen.DIVISION_TERRITORIAL (id_division),
        CONSTRAINT CK_DIVISION_TERRITORIAL_nivel CHECK (nivel BETWEEN 1 AND 4),
        CONSTRAINT CK_DIVISION_TERRITORIAL_estado CHECK (estado IN ('Activo','Inactivo')),
        CONSTRAINT UQ_DIVISION_TERRITORIAL_codigo UNIQUE (id_pais, codigo)
    );
    CREATE INDEX IX_DIVISION_TERRITORIAL_padre ON gen.DIVISION_TERRITORIAL (id_pais, nivel, id_division_padre) INCLUDE (nombre, estado);
END
GO

/* Configuracion de CR y USA */
INSERT INTO gen.PAIS_DIRECCION_CONFIG (id_pais, etiqueta_codigo_postal, patron_codigo_postal, codigo_postal_obligatorio, creado_por)
SELECT i.id_lista_valores_item, c.etiqueta, c.patron, c.obligatorio, N'sistema'
FROM (VALUES (N'CR', N'Codigo postal', N'^\d{5}$', 0), (N'US', N'ZIP code', N'^\d{5}(-\d{4})?$', 1)) AS c(codigo, etiqueta, patron, obligatorio)
JOIN gen.LISTA_VALORES_TIPO t ON t.codigo = N'PAIS'
JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_tipo = t.id_lista_valores_tipo AND i.codigo = c.codigo
WHERE NOT EXISTS (SELECT 1 FROM gen.PAIS_DIRECCION_CONFIG x WHERE x.id_pais = i.id_lista_valores_item);

INSERT INTO gen.PAIS_DIRECCION_NIVEL (id_pais, nivel, nombre, modo, es_obligatorio, creado_por)
SELECT i.id_lista_valores_item, n.nivel, n.nombre, n.modo, n.obligatorio, N'sistema'
FROM (VALUES
    (N'CR', 1, N'Provincia', N'Lista', 1), (N'CR', 2, N'Canton', N'Lista', 1),
    (N'CR', 3, N'Distrito',  N'Lista', 1), (N'CR', 4, N'Barrio', N'Texto', 0),
    (N'US', 1, N'Estado',    N'Lista', 1), (N'US', 2, N'Ciudad', N'Texto', 1)
) AS n(codigo, nivel, nombre, modo, obligatorio)
JOIN gen.LISTA_VALORES_TIPO t ON t.codigo = N'PAIS'
JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_tipo = t.id_lista_valores_tipo AND i.codigo = n.codigo
WHERE NOT EXISTS (SELECT 1 FROM gen.PAIS_DIRECCION_NIVEL x WHERE x.id_pais = i.id_lista_valores_item AND x.nivel = n.nivel);
GO

/* ---------------------------------------------------------------------------
   3. Datos de division territorial
   --------------------------------------------------------------------------- */

/* ---- Costa Rica ---- */
CREATE TABLE #seed (nivel tinyint, codigo nvarchar(20), codigo_padre nvarchar(20) NULL, nombre nvarchar(150));
INSERT INTO #seed VALUES
 (1, N'1', NULL, N'San José'),
 (2, N'101', N'1', N'Central'),
 (3, N'10101', N'101', N'Carmen'),
 (3, N'10102', N'101', N'Merced'),
 (3, N'10103', N'101', N'Hospital'),
 (3, N'10104', N'101', N'Catedral'),
 (3, N'10105', N'101', N'Zapote'),
 (3, N'10106', N'101', N'San Francisco de Dos Rios'),
 (3, N'10107', N'101', N'Uruca'),
 (3, N'10108', N'101', N'Mata Redonda'),
 (3, N'10109', N'101', N'Pavas'),
 (3, N'10110', N'101', N'Hatillo'),
 (3, N'10111', N'101', N'San Sebastián'),
 (2, N'102', N'1', N'Escazú'),
 (3, N'10201', N'102', N'Escazú'),
 (3, N'10202', N'102', N'San Antonio'),
 (3, N'10203', N'102', N'San Rafael'),
 (2, N'103', N'1', N'Desamparados'),
 (3, N'10301', N'103', N'Desamparados'),
 (3, N'10302', N'103', N'San Miguel'),
 (3, N'10303', N'103', N'San Juan de Dios'),
 (3, N'10304', N'103', N'San Rafael Arriba'),
 (3, N'10305', N'103', N'San Rafael Abajo'),
 (3, N'10306', N'103', N'San Antonio'),
 (3, N'10307', N'103', N'Frailes'),
 (3, N'10308', N'103', N'Patarra'),
 (3, N'10309', N'103', N'San Cristobal'),
 (3, N'10310', N'103', N'Rosario'),
 (3, N'10311', N'103', N'Damas'),
 (3, N'10312', N'103', N'Gravilias'),
 (3, N'10313', N'103', N'Los Guido'),
 (2, N'104', N'1', N'Puriscal'),
 (3, N'10401', N'104', N'Santiago'),
 (3, N'10402', N'104', N'Mercedes Sur'),
 (3, N'10403', N'104', N'Barbacoas'),
 (3, N'10404', N'104', N'Grifo Alto'),
 (3, N'10405', N'104', N'San Rafael'),
 (3, N'10406', N'104', N'Candelarita'),
 (3, N'10407', N'104', N'Desamparaditos'),
 (3, N'10408', N'104', N'San Antonio'),
 (3, N'10409', N'104', N'Chires'),
 (2, N'105', N'1', N'Tarrazú'),
 (3, N'10501', N'105', N'San Marcos'),
 (3, N'10502', N'105', N'San Lorenzo'),
 (3, N'10503', N'105', N'San Carlos'),
 (2, N'106', N'1', N'Aserrí'),
 (3, N'10601', N'106', N'Aserrí'),
 (3, N'10602', N'106', N'Tarbaca'),
 (3, N'10603', N'106', N'Vuelta de Jorco'),
 (3, N'10604', N'106', N'San Gabriel'),
 (3, N'10605', N'106', N'Legua'),
 (3, N'10606', N'106', N'Monterrey'),
 (3, N'10607', N'106', N'Salitrillos'),
 (2, N'107', N'1', N'Mora'),
 (3, N'10701', N'107', N'Colón'),
 (3, N'10702', N'107', N'Guayabo'),
 (3, N'10703', N'107', N'Tabarcia'),
 (3, N'10704', N'107', N'Piedras Negras'),
 (3, N'10705', N'107', N'Picagres'),
 (3, N'10706', N'107', N'Jaris'),
 (2, N'108', N'1', N'Goicoechea'),
 (3, N'10801', N'108', N'Guadalupe'),
 (3, N'10802', N'108', N'San Francisco'),
 (3, N'10803', N'108', N'Calle Blancos'),
 (3, N'10804', N'108', N'Mata de Platano'),
 (3, N'10805', N'108', N'Ipís'),
 (3, N'10806', N'108', N'Rancho Redondo'),
 (3, N'10807', N'108', N'Purral'),
 (2, N'109', N'1', N'Santa Ana'),
 (3, N'10901', N'109', N'Santa Ana'),
 (3, N'10902', N'109', N'Salitral'),
 (3, N'10903', N'109', N'Pozos'),
 (3, N'10904', N'109', N'Uruca'),
 (3, N'10905', N'109', N'Piedades'),
 (3, N'10906', N'109', N'Brasil'),
 (2, N'110', N'1', N'Alajuelita'),
 (3, N'11001', N'110', N'Alajuelita'),
 (3, N'11002', N'110', N'San Josecito'),
 (3, N'11003', N'110', N'San Antonio'),
 (3, N'11004', N'110', N'Concepción'),
 (3, N'11005', N'110', N'San Felipe'),
 (2, N'111', N'1', N'Vázquez de Coronado'),
 (3, N'11101', N'111', N'San Isidro'),
 (3, N'11102', N'111', N'San Rafael'),
 (3, N'11103', N'111', N'Dulce Nombre de Jesus'),
 (3, N'11104', N'111', N'Patalillo'),
 (3, N'11105', N'111', N'Cascajal'),
 (2, N'112', N'1', N'Acosta'),
 (3, N'11201', N'112', N'San Ignacio'),
 (3, N'11202', N'112', N'Guaitil'),
 (3, N'11203', N'112', N'Palmichal'),
 (3, N'11204', N'112', N'Cangrejal'),
 (3, N'11205', N'112', N'Sabanillas'),
 (2, N'113', N'1', N'Tibás'),
 (3, N'11301', N'113', N'San Juan'),
 (3, N'11302', N'113', N'Cinco Esquinas'),
 (3, N'11303', N'113', N'Anselmo Llorente'),
 (3, N'11304', N'113', N'Leon XII'),
 (3, N'11305', N'113', N'Colima'),
 (2, N'114', N'1', N'Moravia'),
 (3, N'11401', N'114', N'San Vicente'),
 (3, N'11402', N'114', N'San Jeronimo'),
 (3, N'11403', N'114', N'La Trinidad'),
 (2, N'115', N'1', N'Montes de Oca'),
 (3, N'11501', N'115', N'San Pedro'),
 (3, N'11502', N'115', N'Sabanilla'),
 (3, N'11503', N'115', N'Mercedes'),
 (3, N'11504', N'115', N'San Rafael'),
 (2, N'116', N'1', N'Turrubares'),
 (3, N'11601', N'116', N'San Pablo'),
 (3, N'11602', N'116', N'San Pedro'),
 (3, N'11603', N'116', N'San Juan de Mata'),
 (3, N'11604', N'116', N'San Luis'),
 (3, N'11605', N'116', N'Carara'),
 (2, N'117', N'1', N'Dota'),
 (3, N'11701', N'117', N'Santa María'),
 (3, N'11702', N'117', N'Jardin'),
 (3, N'11703', N'117', N'Copey'),
 (2, N'118', N'1', N'Curridabat'),
 (3, N'11801', N'118', N'Curridabat'),
 (3, N'11802', N'118', N'Granadilla'),
 (3, N'11803', N'118', N'Sanchez'),
 (3, N'11804', N'118', N'Tirrases'),
 (2, N'119', N'1', N'Pérez Zeledón'),
 (3, N'11901', N'119', N'San Isidro de El General'),
 (3, N'11902', N'119', N'El General'),
 (3, N'11903', N'119', N'Daniel Flores'),
 (3, N'11904', N'119', N'Rivas'),
 (3, N'11905', N'119', N'San Pedro'),
 (3, N'11906', N'119', N'Platanares'),
 (3, N'11907', N'119', N'Pejibaye'),
 (3, N'11908', N'119', N'Cajon'),
 (3, N'11909', N'119', N'Baru'),
 (3, N'11910', N'119', N'Rio Nuevo'),
 (3, N'11911', N'119', N'Páramo'),
 (2, N'120', N'1', N'León Cortés Castro'),
 (3, N'12001', N'120', N'San Pablo'),
 (3, N'12002', N'120', N'San Andres'),
 (3, N'12003', N'120', N'Llano Bonito'),
 (3, N'12004', N'120', N'San Isidro'),
 (3, N'12005', N'120', N'Santa Cruz'),
 (3, N'12006', N'120', N'San Antonio'),
 (1, N'2', NULL, N'Alajuela'),
 (2, N'201', N'2', N'Central'),
 (3, N'20101', N'201', N'Alajuela'),
 (3, N'20102', N'201', N'San José'),
 (3, N'20103', N'201', N'Carrizal'),
 (3, N'20104', N'201', N'San Antonio'),
 (3, N'20105', N'201', N'Guácima'),
 (3, N'20106', N'201', N'San Isidro'),
 (3, N'20107', N'201', N'Sabanilla'),
 (3, N'20108', N'201', N'San Rafael'),
 (3, N'20109', N'201', N'Rio Segundo'),
 (3, N'20110', N'201', N'Desamparados'),
 (3, N'20111', N'201', N'Turrucares'),
 (3, N'20112', N'201', N'Tambor'),
 (3, N'20113', N'201', N'Garita'),
 (3, N'20114', N'201', N'Sarapiquí'),
 (2, N'202', N'2', N'San Ramón'),
 (3, N'20201', N'202', N'San Ramón'),
 (3, N'20202', N'202', N'Santiago'),
 (3, N'20203', N'202', N'San Juan'),
 (3, N'20204', N'202', N'Piedades Norte'),
 (3, N'20205', N'202', N'Piedades Sur'),
 (3, N'20206', N'202', N'San Rafael'),
 (3, N'20207', N'202', N'San Isidro'),
 (3, N'20208', N'202', N'Angeles'),
 (3, N'20209', N'202', N'Alfaro'),
 (3, N'20210', N'202', N'Volio'),
 (3, N'20211', N'202', N'Concepción'),
 (3, N'20212', N'202', N'Zapotal'),
 (3, N'20213', N'202', N'Peñas Blancas'),
 (2, N'203', N'2', N'Grecia'),
 (3, N'20301', N'203', N'Grecia'),
 (3, N'20302', N'203', N'San Isidro'),
 (3, N'20303', N'203', N'San José'),
 (3, N'20304', N'203', N'San Roque'),
 (3, N'20305', N'203', N'Tacares'),
 (3, N'20306', N'203', N'Rio Cuarto'),
 (3, N'20307', N'203', N'Puente de Piedra'),
 (3, N'20308', N'203', N'Bolivar'),
 (2, N'204', N'2', N'San Mateo'),
 (3, N'20401', N'204', N'San Mateo'),
 (3, N'20402', N'204', N'Desmonte'),
 (3, N'20403', N'204', N'Jesús María'),
 (3, N'20404', N'204', N'Labrador'),
 (2, N'205', N'2', N'Atenas'),
 (3, N'20501', N'205', N'Atenas'),
 (3, N'20502', N'205', N'Jesús'),
 (3, N'20503', N'205', N'Mercedes'),
 (3, N'20504', N'205', N'San Isidro'),
 (3, N'20505', N'205', N'Concepción'),
 (3, N'20506', N'205', N'San José'),
 (3, N'20507', N'205', N'Santa Eulalia'),
 (3, N'20508', N'205', N'Escobal'),
 (2, N'206', N'2', N'Naranjo'),
 (3, N'20601', N'206', N'Naranjo'),
 (3, N'20602', N'206', N'San Miguel'),
 (3, N'20603', N'206', N'San José'),
 (3, N'20604', N'206', N'Cirrí Sur'),
 (3, N'20605', N'206', N'San Jerónimo'),
 (3, N'20606', N'206', N'San Juan'),
 (3, N'20607', N'206', N'El Rosario'),
 (3, N'20608', N'206', N'Palmitos'),
 (2, N'207', N'2', N'Palmares'),
 (3, N'20701', N'207', N'Palmares'),
 (3, N'20702', N'207', N'Zaragoza'),
 (3, N'20703', N'207', N'Buenos Aires'),
 (3, N'20704', N'207', N'Santiago'),
 (3, N'20705', N'207', N'Candelaria'),
 (3, N'20706', N'207', N'Esquipulas'),
 (3, N'20707', N'207', N'La Granja'),
 (2, N'208', N'2', N'Poás'),
 (3, N'20801', N'208', N'San Pedro'),
 (3, N'20802', N'208', N'San Juan'),
 (3, N'20803', N'208', N'San Rafael'),
 (3, N'20804', N'208', N'Carrillos'),
 (3, N'20805', N'208', N'Sabana Redonda'),
 (2, N'209', N'2', N'Orotina'),
 (3, N'20901', N'209', N'Orotina'),
 (3, N'20902', N'209', N'El Mastate'),
 (3, N'20903', N'209', N'Hacienda Vieja'),
 (3, N'20904', N'209', N'Coyolar'),
 (3, N'20905', N'209', N'La Ceiba'),
 (2, N'210', N'2', N'San Carlos'),
 (3, N'21001', N'210', N'Quesada'),
 (3, N'21002', N'210', N'Florencia'),
 (3, N'21003', N'210', N'Buenavista'),
 (3, N'21004', N'210', N'Aguas Zarcas'),
 (3, N'21005', N'210', N'Venecia'),
 (3, N'21006', N'210', N'Pital'),
 (3, N'21007', N'210', N'La Fortuna'),
 (3, N'21008', N'210', N'La Tigra'),
 (3, N'21009', N'210', N'La Palmera'),
 (3, N'21010', N'210', N'Venado'),
 (3, N'21011', N'210', N'Cutris'),
 (3, N'21012', N'210', N'Monterrey'),
 (3, N'21013', N'210', N'Pocosol'),
 (2, N'211', N'2', N'Zarcero'),
 (3, N'21101', N'211', N'Zarcero'),
 (3, N'21102', N'211', N'Laguna'),
 (3, N'21103', N'211', N'Tapesco'),
 (3, N'21104', N'211', N'Guadalupe'),
 (3, N'21105', N'211', N'Palmira'),
 (3, N'21106', N'211', N'Zapote'),
 (3, N'21107', N'211', N'Brisas'),
 (2, N'212', N'2', N'Sarchí'),
 (3, N'21201', N'212', N'Sarchí Norte'),
 (3, N'21202', N'212', N'Sarchí Sur'),
 (3, N'21203', N'212', N'Toro Amarillo'),
 (3, N'21204', N'212', N'San Pedro'),
 (3, N'21205', N'212', N'Rodriguez'),
 (2, N'213', N'2', N'Upala'),
 (3, N'21301', N'213', N'Upala'),
 (3, N'21302', N'213', N'Aguas Claras'),
 (3, N'21303', N'213', N'San José o Pizote'),
 (3, N'21304', N'213', N'Bijagua'),
 (3, N'21305', N'213', N'Delicias'),
 (3, N'21306', N'213', N'Dos Rios'),
 (3, N'21307', N'213', N'Yolillal'),
 (3, N'21308', N'213', N'Canalete'),
 (2, N'214', N'2', N'Los Chiles'),
 (3, N'21401', N'214', N'Los Chiles'),
 (3, N'21402', N'214', N'Caño Negro'),
 (3, N'21403', N'214', N'El Amparo'),
 (3, N'21404', N'214', N'San Jorge'),
 (2, N'215', N'2', N'Guatuso'),
 (3, N'21501', N'215', N'San Rafael'),
 (3, N'21502', N'215', N'Buenavista'),
 (3, N'21503', N'215', N'Cote'),
 (3, N'21504', N'215', N'Katira'),
 (2, N'216', N'2', N'Río Cuarto'),
 (3, N'21601', N'216', N'Río Cuarto'),
 (1, N'3', NULL, N'Cartago'),
 (2, N'301', N'3', N'Central'),
 (3, N'30101', N'301', N'Oriental'),
 (3, N'30102', N'301', N'Occidental'),
 (3, N'30103', N'301', N'Carmen'),
 (3, N'30104', N'301', N'San Nicolás'),
 (3, N'30105', N'301', N'Aguacaliente o San Francisco'),
 (3, N'30106', N'301', N'Guadalupe o Arenilla'),
 (3, N'30107', N'301', N'Corralillo'),
 (3, N'30108', N'301', N'Tierra Blanca'),
 (3, N'30109', N'301', N'Dulce Nombre'),
 (3, N'30110', N'301', N'Llano Grande'),
 (3, N'30111', N'301', N'Quebradilla'),
 (2, N'302', N'3', N'Paraíso'),
 (3, N'30201', N'302', N'Paraiso'),
 (3, N'30202', N'302', N'Santiago'),
 (3, N'30203', N'302', N'Orosi'),
 (3, N'30204', N'302', N'Cachí'),
 (3, N'30205', N'302', N'Llanos de Santa Lucía'),
 (2, N'303', N'3', N'La Unión'),
 (3, N'30301', N'303', N'Tres Rios'),
 (3, N'30302', N'303', N'San Diego'),
 (3, N'30303', N'303', N'San Juan'),
 (3, N'30304', N'303', N'San Rafael'),
 (3, N'30305', N'303', N'Concepción'),
 (3, N'30306', N'303', N'Dulce Nombre'),
 (3, N'30307', N'303', N'San Ramón'),
 (3, N'30308', N'303', N'Rio Azul'),
 (2, N'304', N'3', N'Jiménez'),
 (3, N'30401', N'304', N'Juan Viñas'),
 (3, N'30402', N'304', N'Tucurrique'),
 (3, N'30403', N'304', N'Pejibaye'),
 (2, N'305', N'3', N'Turrialba'),
 (3, N'30501', N'305', N'Turrialba'),
 (3, N'30502', N'305', N'La Suiza'),
 (3, N'30503', N'305', N'Peralta'),
 (3, N'30504', N'305', N'Santa Cruz'),
 (3, N'30505', N'305', N'Santa Teresita'),
 (3, N'30506', N'305', N'Pavones'),
 (3, N'30507', N'305', N'Tuis'),
 (3, N'30508', N'305', N'Tayutic'),
 (3, N'30509', N'305', N'Santa Rosa'),
 (3, N'30510', N'305', N'Tres Equis'),
 (3, N'30511', N'305', N'La Isabel'),
 (3, N'30512', N'305', N'Chirripó'),
 (2, N'306', N'3', N'Alvarado'),
 (3, N'30601', N'306', N'Pacayas'),
 (3, N'30602', N'306', N'Cervantes'),
 (3, N'30603', N'306', N'Capellades'),
 (2, N'307', N'3', N'Oreamuno'),
 (3, N'30701', N'307', N'San Rafael'),
 (3, N'30702', N'307', N'Cot'),
 (3, N'30703', N'307', N'Potrero Cerrado'),
 (3, N'30704', N'307', N'Cipreses'),
 (3, N'30705', N'307', N'Santa Rosa'),
 (2, N'308', N'3', N'El Guarco'),
 (3, N'30801', N'308', N'El Tejar'),
 (3, N'30802', N'308', N'San Isidro'),
 (3, N'30803', N'308', N'Tobosi'),
 (3, N'30804', N'308', N'Patio de Agua'),
 (1, N'4', NULL, N'Heredia'),
 (2, N'401', N'4', N'Central'),
 (3, N'40101', N'401', N'Heredia'),
 (3, N'40102', N'401', N'Mercedes'),
 (3, N'40103', N'401', N'San Francisco'),
 (3, N'40104', N'401', N'Ulloa'),
 (3, N'40105', N'401', N'Varablanca'),
 (2, N'402', N'4', N'Barva'),
 (3, N'40201', N'402', N'Barva'),
 (3, N'40202', N'402', N'San Pedro'),
 (3, N'40203', N'402', N'San Pablo'),
 (3, N'40204', N'402', N'San Roque'),
 (3, N'40205', N'402', N'Santa Lucía'),
 (3, N'40206', N'402', N'San José de la Montaña'),
 (2, N'403', N'4', N'Santo Domingo'),
 (3, N'40301', N'403', N'Santo Domingo'),
 (3, N'40302', N'403', N'San Vicente'),
 (3, N'40303', N'403', N'San Miguel'),
 (3, N'40304', N'403', N'Paracito'),
 (3, N'40305', N'403', N'Santo Tomás'),
 (3, N'40306', N'403', N'Santa Rosa'),
 (3, N'40307', N'403', N'Tures'),
 (3, N'40308', N'403', N'Para'),
 (2, N'404', N'4', N'Santa Barbara'),
 (3, N'40401', N'404', N'Santa Bárbara'),
 (3, N'40402', N'404', N'San Pedro'),
 (3, N'40403', N'404', N'San Juan'),
 (3, N'40404', N'404', N'Jesús'),
 (3, N'40405', N'404', N'Santo Domingo'),
 (3, N'40406', N'404', N'Puraba'),
 (2, N'405', N'4', N'San Rafael'),
 (3, N'40501', N'405', N'San Rafael'),
 (3, N'40502', N'405', N'San Josecito'),
 (3, N'40503', N'405', N'Santiago'),
 (3, N'40504', N'405', N'Los Ángeles'),
 (3, N'40505', N'405', N'Concepción'),
 (2, N'406', N'4', N'San Isidro'),
 (3, N'40601', N'406', N'San Isidro'),
 (3, N'40602', N'406', N'San José'),
 (3, N'40603', N'406', N'Concepción'),
 (3, N'40604', N'406', N'San Francisco'),
 (2, N'407', N'4', N'Belén'),
 (3, N'40701', N'407', N'San Antonio'),
 (3, N'40702', N'407', N'La Ribera'),
 (3, N'40703', N'407', N'La Asuncion'),
 (2, N'408', N'4', N'Flores'),
 (3, N'40801', N'408', N'San Joaquín'),
 (3, N'40802', N'408', N'Barrantes'),
 (3, N'40803', N'408', N'Llorente'),
 (2, N'409', N'4', N'San Pablo'),
 (3, N'40901', N'409', N'San Pablo'),
 (3, N'40902', N'409', N'Rincon de Sabanilla'),
 (2, N'410', N'4', N'Sarapiquí'),
 (3, N'41001', N'410', N'Puerto Viejo'),
 (3, N'41002', N'410', N'La Virgen'),
 (3, N'41003', N'410', N'Las Horquetas'),
 (3, N'41004', N'410', N'Llanuras del Gaspar'),
 (3, N'41005', N'410', N'Cureña'),
 (1, N'5', NULL, N'Guanacaste'),
 (2, N'501', N'5', N'Liberia'),
 (3, N'50101', N'501', N'Liberia'),
 (3, N'50102', N'501', N'Cañas Dulces'),
 (3, N'50103', N'501', N'Mayorga'),
 (3, N'50104', N'501', N'Nacascolo'),
 (3, N'50105', N'501', N'Curubande'),
 (2, N'502', N'5', N'Nicoya'),
 (3, N'50201', N'502', N'Nicoya');
INSERT INTO #seed VALUES
 (3, N'50202', N'502', N'Mansión'),
 (3, N'50203', N'502', N'San Antonio'),
 (3, N'50204', N'502', N'Quebrada Honda'),
 (3, N'50205', N'502', N'Sámara'),
 (3, N'50206', N'502', N'Nosara'),
 (3, N'50207', N'502', N'Belén de Nosarita'),
 (2, N'503', N'5', N'Santa Cruz'),
 (3, N'50301', N'503', N'Santa Cruz'),
 (3, N'50302', N'503', N'Bolson'),
 (3, N'50303', N'503', N'Veintisiete de Abril'),
 (3, N'50304', N'503', N'Tempate'),
 (3, N'50305', N'503', N'Cartagena'),
 (3, N'50306', N'503', N'Cuajiniquil'),
 (3, N'50307', N'503', N'Diria'),
 (3, N'50308', N'503', N'Cabo Velas'),
 (3, N'50309', N'503', N'Tamarindo'),
 (2, N'504', N'5', N'Bagaces'),
 (3, N'50401', N'504', N'Bagaces'),
 (3, N'50402', N'504', N'La Fortuna'),
 (3, N'50403', N'504', N'Mogote'),
 (3, N'50404', N'504', N'Rio Naranjo'),
 (3, N'50405', N'504', N'Pijije'),
 (2, N'505', N'5', N'Carrillo'),
 (3, N'50501', N'505', N'Filadelfia'),
 (3, N'50502', N'505', N'Palmira'),
 (3, N'50503', N'505', N'Sardinal'),
 (3, N'50504', N'505', N'Belen'),
 (2, N'506', N'5', N'Cañas'),
 (3, N'50601', N'506', N'Cañas'),
 (3, N'50602', N'506', N'Palmira'),
 (3, N'50603', N'506', N'San Miguel'),
 (3, N'50604', N'506', N'Bebedero'),
 (3, N'50605', N'506', N'Porozal'),
 (2, N'507', N'5', N'Abangares'),
 (3, N'50701', N'507', N'Las Juntas'),
 (3, N'50702', N'507', N'Sierra'),
 (3, N'50703', N'507', N'San Juan'),
 (3, N'50704', N'507', N'Colorado'),
 (2, N'508', N'5', N'Tilarán'),
 (3, N'50801', N'508', N'Tilarán'),
 (3, N'50802', N'508', N'Quebrada Grande'),
 (3, N'50803', N'508', N'Tronadora'),
 (3, N'50804', N'508', N'Santa Rosa'),
 (3, N'50805', N'508', N'Líbano'),
 (3, N'50806', N'508', N'Tierras Morenas'),
 (3, N'50807', N'508', N'Arenal'),
 (2, N'509', N'5', N'Nandayure'),
 (3, N'50901', N'509', N'Carmona'),
 (3, N'50902', N'509', N'Santa Rita'),
 (3, N'50903', N'509', N'Zapotal'),
 (3, N'50904', N'509', N'San Pablo'),
 (3, N'50905', N'509', N'Porvenir'),
 (3, N'50906', N'509', N'Bejuco'),
 (2, N'510', N'5', N'La Cruz'),
 (3, N'51001', N'510', N'La Cruz'),
 (3, N'51002', N'510', N'Santa Cecilia'),
 (3, N'51003', N'510', N'La Garita'),
 (3, N'51004', N'510', N'Santa Elena'),
 (2, N'511', N'5', N'Hojancha'),
 (3, N'51101', N'511', N'Hojancha'),
 (3, N'51102', N'511', N'Monte Romo'),
 (3, N'51103', N'511', N'Puerto Carrillo'),
 (3, N'51104', N'511', N'Huacas'),
 (1, N'6', NULL, N'Puntarenas'),
 (2, N'601', N'6', N'Central'),
 (3, N'60101', N'601', N'Puntarenas'),
 (3, N'60102', N'601', N'Pitahaya'),
 (3, N'60103', N'601', N'Chomes'),
 (3, N'60104', N'601', N'Lepanto'),
 (3, N'60105', N'601', N'Paquera'),
 (3, N'60106', N'601', N'Manzanillo'),
 (3, N'60107', N'601', N'Guacimal'),
 (3, N'60108', N'601', N'Barranca'),
 (3, N'60109', N'601', N'Monte Verde'),
 (3, N'60110', N'601', N'Isla del Coco'),
 (3, N'60111', N'601', N'Cóbano'),
 (3, N'60112', N'601', N'Chacarita'),
 (3, N'60113', N'601', N'Chira'),
 (3, N'60114', N'601', N'Acapulco'),
 (3, N'60115', N'601', N'El Roble'),
 (3, N'60116', N'601', N'Arancibia'),
 (2, N'602', N'6', N'Esparza'),
 (3, N'60201', N'602', N'Espíritu Santo'),
 (3, N'60202', N'602', N'San Juan Grande'),
 (3, N'60203', N'602', N'Macacona'),
 (3, N'60204', N'602', N'San Rafael'),
 (3, N'60205', N'602', N'San Jerónimo'),
 (3, N'60206', N'602', N'Caldera'),
 (2, N'603', N'6', N'Buenos Aires'),
 (3, N'60301', N'603', N'Buenos Aires'),
 (3, N'60302', N'603', N'Volcán'),
 (3, N'60303', N'603', N'Potrero Grande'),
 (3, N'60304', N'603', N'Boruca'),
 (3, N'60305', N'603', N'Pilas'),
 (3, N'60306', N'603', N'Colinas'),
 (3, N'60307', N'603', N'Changuena'),
 (3, N'60308', N'603', N'Biolley'),
 (3, N'60309', N'603', N'Brunka'),
 (3, N'60310', N'603', N'Cabagra'),
 (2, N'604', N'6', N'Montes de Oro'),
 (3, N'60401', N'604', N'Miramar'),
 (3, N'60402', N'604', N'La Unión'),
 (3, N'60403', N'604', N'San Isidro'),
 (2, N'605', N'6', N'Osa'),
 (3, N'60501', N'605', N'Puerto Cortés'),
 (3, N'60502', N'605', N'Palmar'),
 (3, N'60503', N'605', N'Sierpe'),
 (3, N'60504', N'605', N'Bahía Ballena'),
 (3, N'60505', N'605', N'Piedras Blancas'),
 (3, N'60506', N'605', N'Bahía Drake'),
 (2, N'606', N'6', N'Quepos'),
 (3, N'60601', N'606', N'Quepos'),
 (3, N'60602', N'606', N'Savegre'),
 (3, N'60603', N'606', N'Naranjito'),
 (2, N'607', N'6', N'Golfito'),
 (3, N'60701', N'607', N'Golfito'),
 (3, N'60702', N'607', N'Puerto Jiménez'),
 (3, N'60703', N'607', N'Guaycara'),
 (3, N'60704', N'607', N'Pavón'),
 (2, N'608', N'6', N'Coto Brus'),
 (3, N'60801', N'608', N'San Vito'),
 (3, N'60802', N'608', N'Sabalito'),
 (3, N'60803', N'608', N'Aguabuena'),
 (3, N'60804', N'608', N'Limoncito'),
 (3, N'60805', N'608', N'Pittier'),
 (2, N'609', N'6', N'Parrita'),
 (3, N'60901', N'609', N'Parrita'),
 (2, N'610', N'6', N'Corredores'),
 (3, N'61001', N'610', N'Corredor'),
 (3, N'61002', N'610', N'La Cuesta'),
 (3, N'61003', N'610', N'Canoas'),
 (3, N'61004', N'610', N'Laurel'),
 (2, N'611', N'6', N'Garabito'),
 (3, N'61101', N'611', N'Jacó'),
 (3, N'61102', N'611', N'Tárcoles'),
 (1, N'7', NULL, N'Limón'),
 (2, N'701', N'7', N'Central'),
 (3, N'70101', N'701', N'Limón'),
 (3, N'70102', N'701', N'Valle La Estrella'),
 (3, N'70103', N'701', N'Rio Blanco'),
 (3, N'70104', N'701', N'Matama'),
 (2, N'702', N'7', N'Pococí'),
 (3, N'70201', N'702', N'Guapiles'),
 (3, N'70202', N'702', N'Jiménez'),
 (3, N'70203', N'702', N'Rita'),
 (3, N'70204', N'702', N'Roxana'),
 (3, N'70205', N'702', N'Cariari'),
 (3, N'70206', N'702', N'Colorado'),
 (3, N'70207', N'702', N'La Colonia'),
 (2, N'703', N'7', N'Siquirres'),
 (3, N'70301', N'703', N'Siquirres'),
 (3, N'70302', N'703', N'Pacuarito'),
 (3, N'70303', N'703', N'Florida'),
 (3, N'70304', N'703', N'Germania'),
 (3, N'70305', N'703', N'El Cairo'),
 (3, N'70306', N'703', N'Alegría'),
 (2, N'704', N'7', N'Talamanca'),
 (3, N'70401', N'704', N'Bratsi'),
 (3, N'70402', N'704', N'Sixaola'),
 (3, N'70403', N'704', N'Cahuita'),
 (3, N'70404', N'704', N'Telire'),
 (2, N'705', N'7', N'Matina'),
 (3, N'70501', N'705', N'Matina'),
 (3, N'70502', N'705', N'Batán'),
 (3, N'70503', N'705', N'Carrandi'),
 (2, N'706', N'7', N'Guácimo'),
 (3, N'70601', N'706', N'Guácimo'),
 (3, N'70602', N'706', N'Mercedes'),
 (3, N'70603', N'706', N'Pocora'),
 (3, N'70604', N'706', N'Rio Jiménez'),
 (3, N'70605', N'706', N'Duacari');

DECLARE @pais bigint = (SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE t.codigo = N'PAIS' AND i.codigo = N'CR');
DECLARE @nivel tinyint = 1;
WHILE @nivel <= 3
BEGIN
    INSERT INTO gen.DIVISION_TERRITORIAL (id_pais, nivel, id_division_padre, codigo, nombre, creado_por)
    SELECT @pais, s.nivel, p.id_division, s.codigo, s.nombre, N'sistema'
    FROM #seed s
    LEFT JOIN gen.DIVISION_TERRITORIAL p ON p.id_pais = @pais AND p.codigo = s.codigo_padre
    WHERE s.nivel = @nivel
      AND (s.codigo_padre IS NULL OR p.id_division IS NOT NULL)
      AND NOT EXISTS (SELECT 1 FROM gen.DIVISION_TERRITORIAL x WHERE x.id_pais = @pais AND x.codigo = s.codigo);
    SET @nivel = @nivel + 1;
END
DROP TABLE #seed;
GO

/* ---- Estados Unidos (estados y DC; codigo USPS) ---- */
CREATE TABLE #seed (nivel tinyint, codigo nvarchar(20), codigo_padre nvarchar(20) NULL, nombre nvarchar(150));
INSERT INTO #seed VALUES
 (1, N'AL', NULL, N'Alabama'),
 (1, N'AK', NULL, N'Alaska'),
 (1, N'AZ', NULL, N'Arizona'),
 (1, N'AR', NULL, N'Arkansas'),
 (1, N'CA', NULL, N'California'),
 (1, N'CO', NULL, N'Colorado'),
 (1, N'CT', NULL, N'Connecticut'),
 (1, N'DE', NULL, N'Delaware'),
 (1, N'DC', NULL, N'District of Columbia'),
 (1, N'FL', NULL, N'Florida'),
 (1, N'GA', NULL, N'Georgia'),
 (1, N'HI', NULL, N'Hawaii'),
 (1, N'ID', NULL, N'Idaho'),
 (1, N'IL', NULL, N'Illinois'),
 (1, N'IN', NULL, N'Indiana'),
 (1, N'IA', NULL, N'Iowa'),
 (1, N'KS', NULL, N'Kansas'),
 (1, N'KY', NULL, N'Kentucky'),
 (1, N'LA', NULL, N'Louisiana'),
 (1, N'ME', NULL, N'Maine'),
 (1, N'MD', NULL, N'Maryland'),
 (1, N'MA', NULL, N'Massachusetts'),
 (1, N'MI', NULL, N'Michigan'),
 (1, N'MN', NULL, N'Minnesota'),
 (1, N'MS', NULL, N'Mississippi'),
 (1, N'MO', NULL, N'Missouri'),
 (1, N'MT', NULL, N'Montana'),
 (1, N'NE', NULL, N'Nebraska'),
 (1, N'NV', NULL, N'Nevada'),
 (1, N'NH', NULL, N'New Hampshire'),
 (1, N'NJ', NULL, N'New Jersey'),
 (1, N'NM', NULL, N'New Mexico'),
 (1, N'NY', NULL, N'New York'),
 (1, N'NC', NULL, N'North Carolina'),
 (1, N'ND', NULL, N'North Dakota'),
 (1, N'OH', NULL, N'Ohio'),
 (1, N'OK', NULL, N'Oklahoma'),
 (1, N'OR', NULL, N'Oregon'),
 (1, N'PA', NULL, N'Pennsylvania'),
 (1, N'RI', NULL, N'Rhode Island'),
 (1, N'SC', NULL, N'South Carolina'),
 (1, N'SD', NULL, N'South Dakota'),
 (1, N'TN', NULL, N'Tennessee'),
 (1, N'TX', NULL, N'Texas'),
 (1, N'UT', NULL, N'Utah'),
 (1, N'VT', NULL, N'Vermont'),
 (1, N'VA', NULL, N'Virginia'),
 (1, N'WA', NULL, N'Washington'),
 (1, N'WV', NULL, N'West Virginia'),
 (1, N'WI', NULL, N'Wisconsin'),
 (1, N'WY', NULL, N'Wyoming');

DECLARE @pais bigint = (SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE t.codigo = N'PAIS' AND i.codigo = N'US');
DECLARE @nivel tinyint = 1;
WHILE @nivel <= 3
BEGIN
    INSERT INTO gen.DIVISION_TERRITORIAL (id_pais, nivel, id_division_padre, codigo, nombre, creado_por)
    SELECT @pais, s.nivel, p.id_division, s.codigo, s.nombre, N'sistema'
    FROM #seed s
    LEFT JOIN gen.DIVISION_TERRITORIAL p ON p.id_pais = @pais AND p.codigo = s.codigo_padre
    WHERE s.nivel = @nivel
      AND (s.codigo_padre IS NULL OR p.id_division IS NOT NULL)
      AND NOT EXISTS (SELECT 1 FROM gen.DIVISION_TERRITORIAL x WHERE x.id_pais = @pais AND x.codigo = s.codigo);
    SET @nivel = @nivel + 1;
END
DROP TABLE #seed;
GO

/* ---------------------------------------------------------------------------
   4. Direccion: pais, niveles y codigo postal (migra provincia/canton/distrito)
   --------------------------------------------------------------------------- */
IF COL_LENGTH('cli.INTERLOCUTOR_DIRECCION', 'id_pais') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_DIRECCION ADD
        id_pais       bigint        NULL,
        nivel1_id     bigint        NULL, nivel1_texto nvarchar(150) NULL,
        nivel2_id     bigint        NULL, nivel2_texto nvarchar(150) NULL,
        nivel3_id     bigint        NULL, nivel3_texto nvarchar(150) NULL,
        nivel4_id     bigint        NULL, nivel4_texto nvarchar(150) NULL,
        codigo_postal nvarchar(20)  NULL;
GO

-- Direcciones existentes: pais Costa Rica y sus textos pasan a nivel 1/2/3.
IF COL_LENGTH('cli.INTERLOCUTOR_DIRECCION', 'provincia') IS NOT NULL
    EXEC (N'
    DECLARE @cr bigint = (SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i
                           JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                          WHERE t.codigo = N''PAIS'' AND i.codigo = N''CR'');
    UPDATE cli.INTERLOCUTOR_DIRECCION
       SET id_pais = @cr, nivel1_texto = provincia, nivel2_texto = canton, nivel3_texto = distrito
     WHERE id_pais IS NULL;');
GO

-- Enlaza los textos migrados con la lista cuando el nombre coincide (sin distinguir tildes ni mayusculas).
UPDATE d SET nivel1_id = v.id_division, nivel1_texto = NULL
FROM cli.INTERLOCUTOR_DIRECCION d
JOIN gen.DIVISION_TERRITORIAL v ON v.id_pais = d.id_pais AND v.nivel = 1
 AND v.nombre = d.nivel1_texto COLLATE Latin1_General_CI_AI
WHERE d.nivel1_id IS NULL AND d.nivel1_texto IS NOT NULL;

UPDATE d SET nivel2_id = v.id_division, nivel2_texto = NULL
FROM cli.INTERLOCUTOR_DIRECCION d
JOIN gen.DIVISION_TERRITORIAL v ON v.id_pais = d.id_pais AND v.nivel = 2 AND v.id_division_padre = d.nivel1_id
 AND v.nombre = d.nivel2_texto COLLATE Latin1_General_CI_AI
WHERE d.nivel2_id IS NULL AND d.nivel2_texto IS NOT NULL;

UPDATE d SET nivel3_id = v.id_division, nivel3_texto = NULL
FROM cli.INTERLOCUTOR_DIRECCION d
JOIN gen.DIVISION_TERRITORIAL v ON v.id_pais = d.id_pais AND v.nivel = 3 AND v.id_division_padre = d.nivel2_id
 AND v.nombre = d.nivel3_texto COLLATE Latin1_General_CI_AI
WHERE d.nivel3_id IS NULL AND d.nivel3_texto IS NOT NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'cli.INTERLOCUTOR_DIRECCION') AND name = N'id_pais' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_DIRECCION WHERE id_pais IS NULL)
    ALTER TABLE cli.INTERLOCUTOR_DIRECCION ALTER COLUMN id_pais bigint NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INTERLOCUTOR_DIRECCION_PAIS')
    ALTER TABLE cli.INTERLOCUTOR_DIRECCION ADD CONSTRAINT FK_INTERLOCUTOR_DIRECCION_PAIS
        FOREIGN KEY (id_pais) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INTERLOCUTOR_DIRECCION_NIVEL1')
    ALTER TABLE cli.INTERLOCUTOR_DIRECCION ADD
        CONSTRAINT FK_INTERLOCUTOR_DIRECCION_NIVEL1 FOREIGN KEY (nivel1_id) REFERENCES gen.DIVISION_TERRITORIAL (id_division),
        CONSTRAINT FK_INTERLOCUTOR_DIRECCION_NIVEL2 FOREIGN KEY (nivel2_id) REFERENCES gen.DIVISION_TERRITORIAL (id_division),
        CONSTRAINT FK_INTERLOCUTOR_DIRECCION_NIVEL3 FOREIGN KEY (nivel3_id) REFERENCES gen.DIVISION_TERRITORIAL (id_division),
        CONSTRAINT FK_INTERLOCUTOR_DIRECCION_NIVEL4 FOREIGN KEY (nivel4_id) REFERENCES gen.DIVISION_TERRITORIAL (id_division);
GO

IF COL_LENGTH('cli.INTERLOCUTOR_DIRECCION', 'provincia') IS NOT NULL
    EXEC (N'ALTER TABLE cli.INTERLOCUTOR_DIRECCION DROP COLUMN provincia, canton, distrito;');
GO

/* ---------------------------------------------------------------------------
   5. Permiso de la pantalla Administracion > Division territorial
   --------------------------------------------------------------------------- */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'DIV_ADMINISTRACION', N'Administrar division territorial', N'Pantalla',
       N'Configurar la estructura de direccion por pais y mantener/importar las divisiones territoriales.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'DIV_ADMINISTRACION');

-- Permiso para mantener la lista PAIS en Listas de Valores.
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_PAIS', N'Mantener lista de valores: Pais', N'Transaccion',
       N'Crear, editar, activar/desactivar y borrar los valores de la lista PAIS.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_PAIS');
GO

DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @id_rol IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION oa
    WHERE oa.codigo IN (N'DIV_ADMINISTRACION', N'LOV_PAIS')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO rao
                       WHERE rao.id_rol_autorizacion = @id_rol AND rao.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT p.codigo AS pais, d.nivel, COUNT(*) AS divisiones
FROM gen.DIVISION_TERRITORIAL d
JOIN gen.LISTA_VALORES_ITEM p ON p.id_lista_valores_item = d.id_pais
GROUP BY p.codigo, d.nivel ORDER BY p.codigo, d.nivel;

SELECT id_direccion, id_pais, nivel1_id, nivel1_texto, nivel2_id, nivel2_texto, nivel3_id, nivel3_texto, detalle
FROM cli.INTERLOCUTOR_DIRECCION;
