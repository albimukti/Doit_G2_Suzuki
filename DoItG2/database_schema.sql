-- ============================================================
-- DO-IT G2 Database Schema
-- PT. Suzuki Indomobil
-- Dibuat: 2026-07-27
-- ============================================================

-- ============================================================
-- AUTH & USER MANAGEMENT
-- ============================================================

CREATE TABLE doit_user (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    user_name       VARCHAR(50)  NOT NULL UNIQUE,
    full_name       VARCHAR(100) NOT NULL,
    email           VARCHAR(100),
    password_hash   VARCHAR(255) NOT NULL,
    user_type       VARCHAR(20)  NOT NULL DEFAULT 'STAFF',  -- ADMIN, STAFF, SUPERVISOR, VIEWER, KITE
    is_active       BIT          NOT NULL DEFAULT 1,
    created_date    DATETIME     NOT NULL DEFAULT GETDATE(),
    last_login      DATETIME,

    -- Module access
    is_admin        BIT NOT NULL DEFAULT 0,
    is_partmaster   BIT NOT NULL DEFAULT 0,
    is_pi           BIT NOT NULL DEFAULT 0,
    is_matrix       BIT NOT NULL DEFAULT 0,
    is_fasilitas    BIT NOT NULL DEFAULT 0,
    is_pkb          BIT NOT NULL DEFAULT 0,

    -- PIB privileges
    pib_sim         BIT NOT NULL DEFAULT 0,  -- Key 81
    pib_sis         BIT NOT NULL DEFAULT 0,  -- Key 84
    pib_authorize_81 BIT NOT NULL DEFAULT 0,
    pib_authorize_84 BIT NOT NULL DEFAULT 0,
    pib_check_81    BIT NOT NULL DEFAULT 0,
    pib_check_84    BIT NOT NULL DEFAULT 0,

    -- PEB privileges
    peb_sim         BIT NOT NULL DEFAULT 0,
    peb_sis         BIT NOT NULL DEFAULT 0,
    peb_authorize_81 BIT NOT NULL DEFAULT 0,
    peb_authorize_84 BIT NOT NULL DEFAULT 0,
    peb_check_81    BIT NOT NULL DEFAULT 0,
    peb_check_84    BIT NOT NULL DEFAULT 0
);

CREATE TABLE doit_audit_log (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    user_name   VARCHAR(50),
    action      VARCHAR(100) NOT NULL,
    module      VARCHAR(50)  NOT NULL,
    document_id VARCHAR(50),
    description VARCHAR(500),
    ip_address  VARCHAR(45),
    is_error    BIT NOT NULL DEFAULT 0,
    created_at  DATETIME NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_audit_log_created ON doit_audit_log (created_at DESC);
CREATE INDEX IX_audit_log_module  ON doit_audit_log (module);

CREATE TABLE doit_setting (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    setting_key VARCHAR(100) NOT NULL UNIQUE,
    value       VARCHAR(2000),
    description VARCHAR(500),
    updated_at  DATETIME NOT NULL DEFAULT GETDATE(),
    updated_by  VARCHAR(50)
);

-- ============================================================
-- PIB (PEMBERITAHUAN IMPOR BARANG) TABLES
-- ============================================================

CREATE TABLE PIB_DOIT_FINAL_HEADER (
    CAR                 VARCHAR(30)  NOT NULL PRIMARY KEY,
    ASAL_DATA           VARCHAR(20),
    KD_ID_IMP           VARCHAR(20),
    ID_IMP              VARCHAR(50),
    NM_IMO              VARCHAR(200),
    AL_IMP              VARCHAR(500),
    KD_ID_PPJK          VARCHAR(20),
    STATUS_IMP          VARCHAR(50),
    KD_API              VARCHAR(20),
    ID_PPJK             VARCHAR(50),
    NM_PPJK             VARCHAR(200),
    NO_API              VARCHAR(50),
    AL_PPJK             VARCHAR(500),
    KD_KTR_PPJK         VARCHAR(50),
    TGL_SKEP_PPJK       VARCHAR(20),
    NO_SKEP_PPJK        VARCHAR(50),
    KD_ID_IND           VARCHAR(20),
    ID_IND              VARCHAR(50),
    NM_IND              VARCHAR(200),
    AL_IND              VARCHAR(500),
    KD_KANTOR           VARCHAR(100),
    JNS_PIB             VARCHAR(20),
    JNS_IMP             VARCHAR(20),
    JNS_BAYAR           VARCHAR(20),
    NEG_PEMASOK         VARCHAR(50),
    NM_PEMASOK          VARCHAR(200),
    AL_PEMASOK          VARCHAR(500),
    NM_ANGKUT           VARCHAR(200),
    CARA_ANGKUT         VARCHAR(20),
    PEL_MUAT            VARCHAR(100),
    PEL_BONGKAR         VARCHAR(100),
    PEL_TRANSIT         VARCHAR(100),
    BENDERA_VOY         VARCHAR(50),
    NO_VOY_FLIGHT       VARCHAR(50),
    TGL_TIBA            VARCHAR(20),
    GUDANG              VARCHAR(100),
    NO_BC11             VARCHAR(50),
    NO_POS_BC11         VARCHAR(50),
    NO_SUB_POS          VARCHAR(50),
    TGL_BC11            VARCHAR(20),
    KD_SKEP_FAS         VARCHAR(50),
    JML_CONT            VARCHAR(20),
    LOK_BAYAR           VARCHAR(20),
    JML_BRG             VARCHAR(20),
    KD_JAMINAN          VARCHAR(20),
    KD_VAL              VARCHAR(20),
    NDPBM               VARCHAR(100),
    FOB                 VARCHAR(100),
    ASURANSI            VARCHAR(100),
    FREIGHT             VARCHAR(100),
    CIF                 VARCHAR(100),
    NETTO               VARCHAR(100),
    BRUTO               VARCHAR(100),
    STATUS              VARCHAR(50),
    TP_NAME             VARCHAR(100),
    EDI_NUMBER          VARCHAR(50),
    WKLOAD              VARCHAR(50),
    KOTA_TTD            VARCHAR(100),
    FL_VALID            VARCHAR(20),
    TGL_TTD             VARCHAR(20),
    NM_TTD              VARCHAR(200),
    NM_KOMP             VARCHAR(100),
    NIP_REKAM           VARCHAR(50),
    WK_REKAM            VARCHAR(50),
    CUST_ID             VARCHAR(50),
    KD_DOK_TUTUP        VARCHAR(50),
    BATCH_ID            INT,
    NO_PEN_PIB          VARCHAR(50),
    NO_PEN_BC23         VARCHAR(50),
    NO_SPPB             VARCHAR(50),
    TGL_PEND_PIB        DATE,
    TGL_SPPB            DATE,
    TGL_PEN_BC23        DATE,
    CREATION_DATE       DATETIME DEFAULT GETDATE(),
    CREATED_BY          INT,
    LAST_UPDATE_DATE    DATE,
    LAST_UPDATED_BY     INT,
    FILE_ID             INT,
    TUJUAN_INVOICE      VARCHAR(50),
    RATE                VARCHAR(100),
    CURRENCY            VARCHAR(50)
);

CREATE TABLE PIB_DOIT_FINAL_HARGA (
    CAR             VARCHAR(30) NOT NULL,
    NDPBM           VARCHAR(18),
    FOB             VARCHAR(18),
    ASURANSI        VARCHAR(18),
    FREIGHT         VARCHAR(18),
    CIF             VARCHAR(18),
    NETTO           VARCHAR(18),
    BRUTO           VARCHAR(18),
    TOT_DIBAYAR     VARCHAR(15),
    TOT_DITG_PEM    VARCHAR(15),
    TOT_DITGH       VARCHAR(15),
    TOT_DIBBS       VARCHAR(15),
    KD_VAL          VARCHAR(10),
    BATCH_ID        INT,
    PRIMARY KEY (CAR)
);

CREATE TABLE PIB_DOIT_FINAL_DETAIL (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(30)  NOT NULL,
    SERIAL      INT          NOT NULL,
    HS_NO       VARCHAR(20),
    GOOD_DESC1  VARCHAR(500),
    GOOD_DESC2  VARCHAR(100),
    GOOD_DESC3  VARCHAR(100),
    ORIGIN_COUNTRY VARCHAR(50),
    UNIT_VAL    DECIMAL(18,4),
    UNIT_TYPE   VARCHAR(50),
    QUANTITY    DECIMAL(18,4),
    CIF_PER_UNIT DECIMAL(18,4),
    KD_FAS      VARCHAR(50),
    BM_TARIF    DECIMAL(10,4),
    BM_NILAI    DECIMAL(18,2),
    PPN_TARIF   DECIMAL(10,4),
    PPN_NILAI   DECIMAL(18,2),
    PPH_TARIF   DECIMAL(10,4),
    PPH_NILAI   DECIMAL(18,2)
);

CREATE INDEX IX_pib_detail_car ON PIB_DOIT_FINAL_DETAIL (CAR);

CREATE TABLE PIB_DOIT_FINAL_DOCUMENT (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(100),
    SERIAL      VARCHAR(100),
    KDFASDTL    VARCHAR(100),
    NOURUT      VARCHAR(100),
    DOKKD       VARCHAR(100),
    DOKNM       VARCHAR(200),
    DOKNO       VARCHAR(100),
    DOKTG       VARCHAR(100),
    KDGROUPDOK  VARCHAR(100),
    NOSERIBRGSKEPCOL VARCHAR(100),
    PARTNO      VARCHAR(100),
    USER_ID     VARCHAR(50)
);

CREATE TABLE PIB_DOIT_FINAL_CONTAINER (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(30),
    NO_CONT     VARCHAR(50),
    UKR_CONT    INT,
    JNS_MUAT    CHAR(1),
    JNS_CONT    VARCHAR(100),
    BATCH_ID    INT
);

CREATE TABLE PIB_DOIT_FINAL_KEMASAN (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(30),
    JML_KMS     INT,
    MERK_KMS    VARCHAR(100),
    JNS_KMS     VARCHAR(50),
    BATCH_ID    INT
);

CREATE TABLE PIB_DOIT_FINAL_PUNGUTAN (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(30),
    KD_PUNGUTAN VARCHAR(10),
    NILAI       BIGINT,
    BATCH_ID    INT
);

CREATE TABLE PIB_DOIT_FINAL_KENDARAAN (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(30),
    SERIAL      INT,
    NORANGKA    VARCHAR(50),
    NOMESIN     VARCHAR(50),
    SILINDER    FLOAT,
    TAHUN       VARCHAR(4),
    FLAGCBU     VARCHAR(1),
    INVOICE_NO  VARCHAR(50)
);

CREATE TABLE PIB_DOIT_FINAL_RESPON (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(30),
    RESKD       VARCHAR(3),
    RESTG       DATE,
    RESWK       VARCHAR(6),
    DOKRESNO    VARCHAR(30),
    DOKRESTG    DATE,
    KPBC        VARCHAR(6),
    PIBNO       VARCHAR(6),
    PIBTG       DATE,
    KDGUDANG    VARCHAR(4),
    PEJABAT1    VARCHAR(20),
    NIP1        VARCHAR(18),
    JABATAN1    VARCHAR(50),
    PEJABAT2    VARCHAR(20),
    NIP2        VARCHAR(18),
    JATUHTEMPO  DATE,
    KOMTG       DATE,
    KOMWK       VARCHAR(6),
    DESKRIPSI   TEXT,
    DIBACA      BIT DEFAULT 0,
    JMKEMAS     FLOAT,
    NOKEMAS     VARCHAR(17),
    NPWPIMP     VARCHAR(16),
    NAMAIMP     VARCHAR(50),
    IDPPJK      VARCHAR(16),
    NAMAPPJK    VARCHAR(50),
    ALAMATPPJK  VARCHAR(70),
    KODEBILL    VARCHAR(100),
    TANGGALBILL VARCHAR(14),
    TANGGALJTTTEMPO VARCHAR(14),
    TANGGALAJU  VARCHAR(14),
    TOTALBAYAR  VARCHAR(18),
    TERBILANG   VARCHAR(70)
);

-- ============================================================
-- PEB (PEMBERITAHUAN EKSPOR BARANG) TABLES
-- ============================================================

CREATE TABLE PEB_DOIT_FINAL_HEADER (
    CAR             VARCHAR(100) NOT NULL PRIMARY KEY,
    JNEKS           INT,
    KATEKS          INT,
    JNPEB           INT,
    IDEKS           INT,
    NPWPEKS         VARCHAR(100),
    NAMAEKS         VARCHAR(100),
    ALMTEKS         VARCHAR(100),
    NEGBELI         VARCHAR(100),
    IDPPJK          VARCHAR(100),
    NPWPPPJK        VARCHAR(100),
    NAMAPPJK        VARCHAR(100),
    MODA            VARCHAR(100),
    TGEKS           DATE,
    CARRIER         VARCHAR(100),
    VOY             VARCHAR(100),
    NOSHINORD       VARCHAR(100),
    TGSHIPORD       VARCHAR(100),
    PELMUAT         VARCHAR(100),
    PELBONGKAR      VARCHAR(100),
    PELTRANSIT      VARCHAR(100),
    NOINV           VARCHAR(100),
    TGINV           DATE,
    PROPBRG         INT,
    NEGTUJU         VARCHAR(100),
    KDVAL           VARCHAR(100),
    KDHRG           INT,
    NILINV          BIGINT,
    FREIGHT         BIGINT,
    ASURANSI        BIGINT,
    FOB             BIGINT,
    JMCONT          INT,
    BRUTO           BIGINT,
    NETTO           BIGINT,
    JMBRG           INT,
    KDKTR           VARCHAR(50),
    NILKURS         BIGINT,
    CREATED_DATE    DATETIME DEFAULT GETDATE(),
    CREATED_BY      INT,
    STATUS          INT DEFAULT 0  -- 0=DRAFT, 1=PENDING, 2=SENT, 3=APPROVED, 4=REJECTED
);

CREATE TABLE PEB_DOIT_FINAL_DETAIL (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(100),
    SERIBRG     INT,
    HS          INT,
    URBRG1      VARCHAR(500),
    URBRG2      VARCHAR(100),
    KDBARG      VARCHAR(100),
    JMKOLI      INT,
    JNKOLI      VARCHAR(100),
    DNILINV     BIGINT,
    FOBPERBRG   BIGINT,
    FOBPERSAT   BIGINT,
    JMSATUAN    INT,
    JNSATUAN    VARCHAR(100),
    NETDET      BIGINT,
    NEGASAL     VARCHAR(20),
    CREATED_DATE DATE,
    CREATED_BY  INT
);

CREATE TABLE PEB_DOIT_FINAL_DOCUMENT (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(100),
    KDDOK       VARCHAR(50),
    NODOK       VARCHAR(100),
    TGDOK       DATE,
    CREATED_DATE DATE,
    CREATED_BY  INT
);

CREATE TABLE PEB_DOIT_FINAL_RESPON (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    CAR         VARCHAR(100),
    RESKD       VARCHAR(10),
    RESTG       DATE,
    NOPEN       VARCHAR(50),
    TGPEN       DATE,
    DESKRIPSI   VARCHAR(500),
    DIBACA      BIT DEFAULT 0
);

CREATE INDEX IX_peb_respon_car ON PEB_DOIT_FINAL_RESPON (CAR);

-- ============================================================
-- CEISA 4.0 INTEGRATION TABLES (Ekstensi Integrasi)
-- ============================================================

-- Menyimpan token akses OAuth2 dari Bea Cukai
CREATE TABLE CEISA_API_TOKEN (
    ID              INT IDENTITY(1,1) PRIMARY KEY,
    TOKEN_TYPE      VARCHAR(20)   NOT NULL DEFAULT 'Bearer',
    ACCESS_TOKEN    VARCHAR(2000) NOT NULL,
    REFRESH_TOKEN   VARCHAR(2000),
    EXPIRES_AT      DATETIME      NOT NULL,
    SCOPE           VARCHAR(200),
    CREATED_AT      DATETIME      NOT NULL DEFAULT GETDATE(),
    UPDATED_AT      DATETIME      NOT NULL DEFAULT GETDATE(),
    IS_ACTIVE       BIT           NOT NULL DEFAULT 1
);

-- Mencatat seluruh payload yang dikirim dan diterima dari CEISA
CREATE TABLE CEISA_PAYLOAD_LOG (
    ID              BIGINT IDENTITY(1,1) PRIMARY KEY,
    DIRECTION       VARCHAR(10)   NOT NULL,  -- OUTBOUND / INBOUND
    DOC_TYPE        VARCHAR(10)   NOT NULL,  -- PIB / PEB
    CAR             VARCHAR(100),
    ENDPOINT_URL    VARCHAR(500),
    HTTP_METHOD     VARCHAR(10),
    REQUEST_BODY    NVARCHAR(MAX),
    RESPONSE_BODY   NVARCHAR(MAX),
    HTTP_STATUS     INT,
    ERROR_MESSAGE   VARCHAR(1000),
    DURATION_MS     INT,
    CREATED_AT      DATETIME      NOT NULL DEFAULT GETDATE(),
    CREATED_BY      VARCHAR(50)
);

CREATE INDEX IX_ceisa_log_car    ON CEISA_PAYLOAD_LOG (CAR);
CREATE INDEX IX_ceisa_log_date   ON CEISA_PAYLOAD_LOG (CREATED_AT DESC);

-- Antrean pengiriman otomatis dengan mekanisme retry
CREATE TABLE CEISA_QUEUE_OUTBOX (
    ID              BIGINT IDENTITY(1,1) PRIMARY KEY,
    DOC_TYPE        VARCHAR(10)   NOT NULL,  -- PIB / PEB
    CAR             VARCHAR(100)  NOT NULL,
    PAYLOAD         NVARCHAR(MAX) NOT NULL,
    STATUS          VARCHAR(20)   NOT NULL DEFAULT 'PENDING',  -- PENDING, PROCESSING, SENT, FAILED
    RETRY_COUNT     INT           NOT NULL DEFAULT 0,
    MAX_RETRIES     INT           NOT NULL DEFAULT 5,
    NEXT_RETRY_AT   DATETIME,
    LAST_ERROR      VARCHAR(1000),
    CREATED_AT      DATETIME      NOT NULL DEFAULT GETDATE(),
    UPDATED_AT      DATETIME      NOT NULL DEFAULT GETDATE(),
    SENT_AT         DATETIME
);

CREATE INDEX IX_ceisa_queue_status ON CEISA_QUEUE_OUTBOX (STATUS, NEXT_RETRY_AT);

-- Menyimpan riwayat kurs pajak harian resmi (KMK) dari CEISA/INSW
CREATE TABLE CEISA_MASTER_KURS (
    ID              INT IDENTITY(1,1) PRIMARY KEY,
    KD_VALUTA       VARCHAR(5)    NOT NULL,
    NM_VALUTA       VARCHAR(50),
    NILAI_KURS      DECIMAL(18,4) NOT NULL,
    TGL_BERLAKU     DATE          NOT NULL,
    TGL_AKHIR       DATE,
    SUMBER          VARCHAR(20)   DEFAULT 'KMK',  -- KMK / INSW
    CREATED_AT      DATETIME      NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_ceisa_kurs_val ON CEISA_MASTER_KURS (KD_VALUTA, TGL_BERLAKU DESC);

-- ============================================================
-- SALDO & LAPORAN KITE
-- ============================================================

CREATE TABLE DOIT_KITE_SALDO (
    ID              INT IDENTITY(1,1) PRIMARY KEY,
    NO_SKEP         VARCHAR(50)   NOT NULL,
    PART_NO         VARCHAR(50),
    PART_NAME       VARCHAR(300),
    HS_CODE         VARCHAR(10),
    SATUAN          VARCHAR(10),
    SALDO_AWAL      DECIMAL(18,4) NOT NULL DEFAULT 0,
    PEMASUKAN       DECIMAL(18,4) NOT NULL DEFAULT 0,
    PENGELUARAN     DECIMAL(18,4) NOT NULL DEFAULT 0,
    SALDO_AKHIR     AS (SALDO_AWAL + PEMASUKAN - PENGELUARAN),
    PERIODE_BULAN   INT,
    PERIODE_TAHUN   INT,
    CREATED_AT      DATETIME      NOT NULL DEFAULT GETDATE(),
    UPDATED_BY      VARCHAR(50)
);

CREATE TABLE DOIT_KITE_LAPORAN (
    ID              INT IDENTITY(1,1) PRIMARY KEY,
    NO_SKEP         VARCHAR(50)   NOT NULL,
    JENIS_LAPORAN   VARCHAR(20)   NOT NULL,  -- BCL_KT01, BCL_KT02, etc.
    PERIODE_DARI    DATE,
    PERIODE_SAMPAI  DATE,
    STATUS          VARCHAR(20)   DEFAULT 'DRAFT',  -- DRAFT, SUBMITTED, APPROVED
    FILE_PATH       VARCHAR(500),
    CREATED_AT      DATETIME      NOT NULL DEFAULT GETDATE(),
    CREATED_BY      VARCHAR(50)
);

-- ============================================================
-- MASTER DATA TABLES
-- ============================================================

CREATE TABLE DOIT_MASTER_PART (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    PART_NO     VARCHAR(50)  NOT NULL UNIQUE,
    PART_NAME   VARCHAR(300) NOT NULL,
    HS_CODE     VARCHAR(10),
    SATUAN      VARCHAR(10),
    SUBINVENTORY VARCHAR(20),
    PLANT       VARCHAR(10),
    NEGASAL     VARCHAR(5),
    IS_ACTIVE   BIT NOT NULL DEFAULT 1,
    CREATED_DATE DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    CREATED_BY  INT
);

CREATE TABLE DOIT_MASTER_DOKUMEN_PIB (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    KD_DOK      VARCHAR(10) NOT NULL UNIQUE,
    NM_DOK      VARCHAR(200),
    IS_ACTIVE   BIT NOT NULL DEFAULT 1
);

CREATE TABLE DOIT_MASTER_DOKUMEN_PEB (
    ID          INT IDENTITY(1,1) PRIMARY KEY,
    KD_DOK      VARCHAR(10) NOT NULL UNIQUE,
    NM_DOK      VARCHAR(200),
    IS_ACTIVE   BIT NOT NULL DEFAULT 1
);

-- ============================================================
-- LAPORAN / INVENTORY TABLES
-- ============================================================

CREATE TABLE NPCS_LAPITINV_A (
    ID                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    CAR                 VARCHAR(200),
    CREATION_DATE       DATE,
    NO_PIB              VARCHAR(50),
    TGL_PIB             DATE,
    RR_NO               VARCHAR(1000),
    RR_DATE             DATE,
    INVOICE_NO          VARCHAR(1000),
    PART_NO             VARCHAR(20),
    PART_NAME           VARCHAR(300),
    SATUAN              VARCHAR(4),
    QTY_RCV             INT,
    AMOUNT              FLOAT,
    CCY_CODE            VARCHAR(4),
    SUBINVENTORY        VARCHAR(20),
    NEG_PEMASOK         VARCHAR(10),
    PLANT               VARCHAR(10),
    NO_SERI             VARCHAR(6),
    LAST_GENERATED_DATE DATE,
    USER_ID             VARCHAR(5)
);

CREATE TABLE NPCS_LAPITINV_E (
    ID              BIGINT IDENTITY(1,1) PRIMARY KEY,
    NO_AJU          VARCHAR(100),
    NO_PEB          INT,
    TGL_PEB         DATE,
    NO_INVOICE      VARCHAR(4000),
    TGL_INVOICE     VARCHAR(4000),
    PEMBELI         VARCHAR(100),
    NEGARA_TUJUAN   VARCHAR(100),
    URBRG2          VARCHAR(800),
    SATUAN          VARCHAR(100),
    JUMLAH          INT,
    CCY_CODE        VARCHAR(100),
    NILINV          BIGINT,
    KODE_BARANG     VARCHAR(100)
);

-- ============================================================
-- DEFAULT DATA SEED
-- ============================================================

-- Insert default admin user (password: Admin@123)
INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active, is_admin,
    pib_sim, pib_sis, peb_sim, peb_sis, pib_authorize_81, pib_authorize_84,
    peb_authorize_81, peb_authorize_84, pib_check_81, pib_check_84, peb_check_81, peb_check_84)
VALUES ('admin', 'Administrator', 'admin@suzuki.co.id',
    '$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6',  -- BCrypt of 'Admin@123'
    'ADMIN', 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);

-- Insert requested staff user: DINDA (password: Admin@123)
INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active,
    pib_sim, pib_sis, peb_sim, peb_sis, is_partmaster, is_pi, is_matrix, is_fasilitas, is_pkb)
VALUES ('dinda', 'DINDA staff', 'dinda@suzuki.co.id',
    '$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6',
    'STAFF', 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);

-- Insert requested supervisor user: HERU (password: Admin@123)
INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active,
    pib_sim, pib_sis, peb_sim, peb_sis, pib_authorize_81, pib_authorize_84,
    peb_authorize_81, peb_authorize_84, is_partmaster, is_pi, is_matrix, is_fasilitas, is_pkb)
VALUES ('heru', 'HERU Supervasior', 'heru@suzuki.co.id',
    '$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6',
    'SUPERVISOR', 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);

-- Insert requested admin user: Rizki (password: Admin@123)
INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active, is_admin,
    pib_sim, pib_sis, peb_sim, peb_sis, pib_authorize_81, pib_authorize_84,
    peb_authorize_81, peb_authorize_84, pib_check_81, pib_check_84, peb_check_81, peb_check_84,
    is_partmaster, is_pi, is_matrix, is_fasilitas, is_pkb)
VALUES ('rizki', 'Rizki Admin', 'rizki@suzuki.co.id',
    '$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6',
    'ADMIN', 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);

-- Settings
INSERT INTO doit_setting (setting_key, value, description) VALUES
('APP_VERSION', '2.0.0', 'Versi aplikasi'),
('COMPANY_NAME', 'PT. Suzuki Indomobil', 'Nama perusahaan'),
('USE_MOCK_SILO', 'true', 'Gunakan mock data untuk SILO'),
('CEISA_BASE_URL', '', 'URL API CEISA'),
('INSW_BASE_URL', '', 'URL API INSW');

GO
PRINT 'DO-IT G2 Database Schema created successfully.'
