# Powershell script to seed 1,000 records into SQL Server database DO_IT_G2
$connectionString = "Server=(localdb)\MSSQLLocalDB;Database=DO_IT_G2;Integrated Security=True;TrustServerCertificate=True;"

Add-Type -AssemblyName "System.Data"

$conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
$conn.Open()

Write-Host "Connected to SQL Server DO_IT_G2 database. Seeding 1,000 test records..." -ForegroundColor Green

# 1. Seed 100 Master Part Records
Write-Host "Seeding 100 Master Part Suzuki records..."
$parts = @(
    @{No="13780-68K00"; Name="ELEMENT COMP AIR CLEANER"; Hs="8421.31.20"; Unit="PCE"; Plant="P101"},
    @{No="16510-61A01"; Name="FILTER ASSY ENGINE OIL"; Hs="8421.23.10"; Unit="PCE"; Plant="P101"},
    @{No="09482-M1501"; Name="SPARK PLUG KR6A-10"; Hs="8511.10.00"; Unit="SET"; Plant="P102"},
    @{No="55810-77M00"; Name="PAD SET FRONT BRAKE"; Hs="8708.30.20"; Unit="SET"; Plant="P101"},
    @{No="43401-77M00"; Name="BEARING FRONT WHEEL HUB"; Hs="8482.10.00"; Unit="PCE"; Plant="P103"},
    @{No="17700-77M00"; Name="RADIATOR ASSY ENGINE COOLING"; Hs="8708.91.10"; Unit="PCE"; Plant="P101"},
    @{No="31100-77M00"; Name="MOTOR ASSY STARTING"; Hs="8511.40.00"; Unit="PCE"; Plant="P102"},
    @{No="31400-77M00"; Name="GENERATOR ASSY ALTERNATOR"; Hs="8511.50.00"; Unit="PCE"; Plant="P102"},
    @{No="22100-77M00"; Name="CLUTCH COVER ASSY"; Hs="8708.93.50"; Unit="PCE"; Plant="P101"},
    @{No="22400-77M00"; Name="DISC CLUTCH FRICTION"; Hs="8708.93.50"; Unit="PCE"; Plant="P101"}
)

$rnd = New-Object Random
for ($i = 1; $i -le 100; $i++) {
    $base = $parts[$i % $parts.Count]
    $partNo = "$($base.No)-$($i.ToString('D3'))"
    $partName = "$($base.Name) TYPE-$($i)"
    $hs = $base.Hs
    $unit = $base.Unit
    $plant = $base.Plant
    
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM DOIT_MASTER_PART WHERE PART_NO = @PartNo)
BEGIN
    INSERT INTO DOIT_MASTER_PART (PART_NO, PART_NAME, HS_CODE, SATUAN, SUBINVENTORY, PLANT, NEGASAL, IS_ACTIVE, CREATED_DATE)
    VALUES (@PartNo, @PartName, @Hs, @Unit, 'MAIN_WH', @Plant, 'JP', 1, GETDATE())
END
"@
    $cmd.Parameters.AddWithValue("@PartNo", $partNo) | Out-Null
    $cmd.Parameters.AddWithValue("@PartName", $partName) | Out-Null
    $cmd.Parameters.AddWithValue("@Hs", $hs) | Out-Null
    $cmd.Parameters.AddWithValue("@Unit", $unit) | Out-Null
    $cmd.Parameters.AddWithValue("@Plant", $plant) | Out-Null
    $cmd.ExecuteNonQuery() | Out-Null
}

# 2. Seed 500 PIB (Impor) Headers & Details
Write-Host "Seeding 500 PIB Impor records..."
$suppliers = @(
    @{Name="SUZUKI MOTOR CORPORATION"; Neg="JP"; Alm="300 TAKATSUKA-CHO, MINAMI-KU, HAMAMATSU-SHI, SHIZUOKA"},
    @{Name="DENSO CORPORATION"; Neg="JP"; Alm="1-1 SHOWA-CHO, KARIYA-SHI, AICHI-KEN"},
    @{Name="AISIN SEIKI CO LTD"; Neg="JP"; Alm="2-1 ASAHI-MACHI, KARIYA-SHI, AICHI-KEN"},
    @{Name="MARUTI SUZUKI INDIA LIMITED"; Neg="IN"; Alm="NELSON MANDELA ROAD, VASANT KUNJ, NEW DELHI"},
    @{Name="THAI SUZUKI MOTOR CO LTD"; Neg="TH"; Alm="31/1 MOO 2, RANGSIT-ONGKARAK ROAD, THANYABURI, PATHUMTHANI"}
)
$statuses = @("DRAFT", "PENDING", "SENT", "APPROVED", "REJECTED")

$trans = $conn.BeginTransaction()
try {
    for ($i = 1; $i -le 500; $i++) {
        $car = "00003001062620260714" + $i.ToString("D4")
        $sup = $suppliers[$i % $suppliers.Count]
        $st = $statuses[$i % $statuses.Count]
        $dateTiba = (Get-Date).AddDays(-($i % 60)).ToString("yyyyMMdd")
        $invNo = "INV-JP-2026-" + $i.ToString("D5")
        
        $nopen = if ($st -eq "APPROVED") { "300" + $i.ToString("D5") } else { "" }
        $sppb = if ($st -eq "APPROVED") { "SPPB-010100-" + $i.ToString("D5") } else { "" }

        $cmdHdr = $conn.CreateCommand()
        $cmdHdr.Transaction = $trans
        $cmdHdr.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car)
BEGIN
    INSERT INTO PIB_DOIT_FINAL_HEADER (
        CAR, ASAL_DATA, KD_ID_IMP, ID_IMP, NM_IMO, AL_IMP, STATUS_IMP,
        KD_API, ID_PPJK, NM_PPJK, KD_KANTOR, JNS_PIB, JNS_IMP, JNS_BAYAR,
        NEG_PEMASOK, NM_PEMASOK, AL_PEMASOK, NM_ANGKUT, CARA_ANGKUT, PEL_MUAT, PEL_BONGKAR,
        NO_VOY_FLIGHT, TGL_TIBA, NO_BC11, TGL_BC11, JML_CONT, JML_BRG,
        NO_PEN_PIB, NO_SPPB, TGL_PEND_PIB, TGL_SPPB, CREATION_DATE, STATUS, CURRENCY, RATE
    ) VALUES (
        @Car, '1', '0', '010000354091000', 'PT SUZUKI INDOMOBIL MOTOR', 'JL. RAYA BEKASI KM 29, BEKASI', 'IU',
        '1', '010000123456000', 'PT LOGISTIK PRIMA EXPRESS', '010100', '1', '1', '1',
        @NegPemasok, @NmPemasok, @AlmPemasok, 'WAN HAI 312', '1', 'JPTYO', 'IDTPP',
        'V-2026E', @TglTiba, '001234', @TglTiba, '2', '5',
        @Nopen, @Sppb, GETDATE(), GETDATE(), GETDATE(), @Status, 'USD', '15850'
    )
END
"@
        $cmdHdr.Parameters.AddWithValue("@Car", $car) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@NegPemasok", $sup.Neg) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@NmPemasok", $sup.Name) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@AlmPemasok", $sup.Alm) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@TglTiba", $dateTiba) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@Nopen", $nopen) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@Sppb", $sppb) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@Status", $st) | Out-Null
        $cmdHdr.ExecuteNonQuery() | Out-Null

        # Seed Detail for this PIB
        $cmdDtl = $conn.CreateCommand()
        $cmdDtl.Transaction = $trans
        $cmdDtl.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM PIB_DOIT_FINAL_DETAIL WHERE CAR = @Car)
BEGIN
    INSERT INTO PIB_DOIT_FINAL_DETAIL (
        CAR, SERIAL, HS_NO, GOOD_DESC1, ORIGIN_COUNTRY, UNIT_VAL, UNIT_TYPE, QUANTITY, CIF_PER_UNIT, BM_TARIF, BM_NILAI, PPN_TARIF, PPN_NILAI, PPH_TARIF, PPH_NILAI
    ) VALUES (
        @Car, 1, '8421.31.20', 'SUZUKI AUTOMOTIVE SPAREPARTS - AIR CLEANER ELEMENT', @NegPemasok, 25.50, 'PCE', 500, 25.50, 5.0, 637.50, 11.0, 1402.50, 2.5, 318.75
    )
END
"@
        $cmdDtl.Parameters.AddWithValue("@Car", $car) | Out-Null
        $cmdDtl.Parameters.AddWithValue("@NegPemasok", $sup.Neg) | Out-Null
        $cmdDtl.ExecuteNonQuery() | Out-Null
    }
    $trans.Commit()
    Write-Host "500 PIB records committed." -ForegroundColor Cyan
} catch {
    $trans.Rollback()
    Write-Host "Error seeding PIB: $_" -ForegroundColor Red
}

# 3. Seed 300 PEB (Ekspor) Headers & Details
Write-Host "Seeding 300 PEB Ekspor records..."
$buyers = @(
    @{Name="BOUSTEAD SDN BERHAD"; Neg="MY"; Alm="LEVEL 20, MENARA BOUSTEAD, KUALA LUMPUR"};
    @{Name="SUZUKI PHILIPPINES INC"; Neg="PH"; Alm="126 PROGRESS AVENUE, CARMELRAY INDUSTRIAL PARK 1, CANLUBANG, CALAMBA CITY"};
    @{Name="CAMBODIA SUZUKI MOTOR CO LTD"; Neg="KH"; Alm="NATIONAL ROAD NO 4, PHUM CHOM CHAU, PHNOM PENH"};
    @{Name="SUZUKI VIETNAM CORPORATION"; Neg="VN"; Alm="LONG BINH INDUSTRIAL ZONE, BIEN HOA, DONG NAI"}
)

$transPeb = $conn.BeginTransaction()
try {
    for ($i = 1; $i -le 300; $i++) {
        $car = "00003001062620260715" + $i.ToString("D4")
        $buy = $buyers[$i % $buyers.Count]
        $st = ($i % 5) # 0=DRAFT, 1=PENDING, 2=SENT, 3=APPROVED, 4=REJECTED
        $invNo = "EXP-SUZUKI-2026-" + $i.ToString("D5")
        
        $cmdHdr = $conn.CreateCommand()
        $cmdHdr.Transaction = $transPeb
        $cmdHdr.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM PEB_DOIT_FINAL_HEADER WHERE CAR = @Car)
BEGIN
    INSERT INTO PEB_DOIT_FINAL_HEADER (
        CAR, JNEKS, KATEKS, JNPEB, NPWPEKS, NAMAEKS, ALMTEKS, NEGBELI,
        PELMUAT, PELBONGKAR, NOINV, TGINV, KDVAL, NILINV, FOB, JMCONT, BRUTO, NETTO, JMBRG, KDKTR, CREATED_DATE, STATUS
    ) VALUES (
        @Car, 1, 1, 1, '010000354091000', 'PT SUZUKI INDOMOBIL MOTOR', 'JL RAYA BEKASI KM 29, BEKASI', @NegBeli,
        'IDTPP', 'MYKUL', @NoInv, GETDATE(), 'USD', 45000, 45000, 1, 12000, 10500, 2, '010100', GETDATE(), @Status
    )
END
"@
        $cmdHdr.Parameters.AddWithValue("@Car", $car) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@NegBeli", $buy.Neg) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@NoInv", $invNo) | Out-Null
        $cmdHdr.Parameters.AddWithValue("@Status", $st) | Out-Null
        $cmdHdr.ExecuteNonQuery() | Out-Null

        # Seed Detail for this PEB
        $cmdDtl = $conn.CreateCommand()
        $cmdDtl.Transaction = $transPeb
        $cmdDtl.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM PEB_DOIT_FINAL_DETAIL WHERE CAR = @Car)
BEGIN
    INSERT INTO PEB_DOIT_FINAL_DETAIL (
        CAR, SERIBRG, HS, URBRG1, KDBARG, JMKOLI, JNKOLI, DNILINV, FOBPERBRG, JMSATUAN, JNSATUAN, NETDET, NEGASAL, CREATED_DATE
    ) VALUES (
        @Car, 1, 87032110, 'SUZUKI ERTIGA / CARRY EXPORT COMPLETE VEHICLE', 'V-ERTIGA-2026', 10, 'CKE', 45000, 45000, 10, 'UNT', 10500, 'ID', GETDATE()
    )
END
"@
        $cmdDtl.Parameters.AddWithValue("@Car", $car) | Out-Null
        $cmdDtl.ExecuteNonQuery() | Out-Null
    }
    $transPeb.Commit()
    Write-Host "300 PEB records committed." -ForegroundColor Cyan
} catch {
    $transPeb.Rollback()
    Write-Host "Error seeding PEB: $_" -ForegroundColor Red
}

# 4. Seed 100 Audit Log Records
Write-Host "Seeding 100 Audit Log records..."
$users = @("admin", "dinda", "heru", "rizki")
$actions = @(
    @{Act="LOGIN"; Mod="AUTH"; Desc="User berhasil melakukan autentikasi login"},
    @{Act="CREATE_PIB"; Mod="PIB"; Desc="Pembuatan draf dokumen PIB baru"},
    @{Act="CREATE_PEB"; Mod="PEB"; Desc="Pembuatan draf dokumen PEB baru"},
    @{Act="SYNC_SILO_PIB"; Mod="SILO"; Desc="Sinkronisasi data impor dari database SILO Oracle"},
    @{Act="SYNC_SILO_PEB"; Mod="SILO"; Desc="Sinkronisasi data ekspor dari database SILO Oracle"},
    @{Act="SEND_CEISA_PIB"; Mod="CEISA"; Desc="Transmisi dokumen PIB ke gateway CEISA 4.0 Bea Cukai"},
    @{Act="SEND_CEISA_PEB"; Mod="CEISA"; Desc="Transmisi dokumen PEB ke gateway CEISA 4.0 Bea Cukai"},
    @{Act="UPLOAD_EXCEL_PIB"; Mod="PIB"; Desc="Upload data barang PIB dari file Excel template"}
)

$transLog = $conn.BeginTransaction()
try {
    for ($i = 1; $i -le 100; $i++) {
        $u = $users[$i % $users.Count]
        $a = $actions[$i % $actions.Count]
        $carNo = "00003001062620260714" + ($i * 5).ToString("D4")
        $dateLog = (Get-Date).AddHours(-($i * 2))
        
        $cmdLog = $conn.CreateCommand()
        $cmdLog.Transaction = $transLog
        $cmdLog.CommandText = @"
INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
VALUES (@User, @Action, @Module, @DocId, @Desc, '127.0.0.1', @CreatedAt)
"@
        $cmdLog.Parameters.AddWithValue("@User", $u) | Out-Null
        $cmdLog.Parameters.AddWithValue("@Action", $a.Act) | Out-Null
        $cmdLog.Parameters.AddWithValue("@Module", $a.Mod) | Out-Null
        $cmdLog.Parameters.AddWithValue("@DocId", $carNo) | Out-Null
        $cmdLog.Parameters.AddWithValue("@Desc", $a.Desc) | Out-Null
        $cmdLog.Parameters.AddWithValue("@CreatedAt", $dateLog) | Out-Null
        $cmdLog.ExecuteNonQuery() | Out-Null
    }
    $transLog.Commit()
    Write-Host "100 Audit Log records committed." -ForegroundColor Cyan
} catch {
    $transLog.Rollback()
    Write-Host "Error seeding Audit Log: $_" -ForegroundColor Red
}

$conn.Close()
Write-Host "SUCCESSFULLY SEEDED 1,000 TEST RECORDS INTO DO_IT_G2 DATABASE!" -ForegroundColor Green
