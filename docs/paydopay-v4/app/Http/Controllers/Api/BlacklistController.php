<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Symfony\Component\HttpFoundation\StreamedResponse;
use PhpOffice\PhpSpreadsheet\Spreadsheet;
use PhpOffice\PhpSpreadsheet\Writer\Xlsx;
use PhpOffice\PhpSpreadsheet\Style\Alignment;
use PhpOffice\PhpSpreadsheet\Style\Fill;
use PhpOffice\PhpSpreadsheet\Style\Border;

class BlacklistController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $query = DB::table('blacklist')->orderByDesc('id');

        if ($request->filled('search')) {
            $search = $request->search;
            $query->where(function ($q) use ($search) {
                $q->where('val', 'like', "%{$search}%")
                  ->orWhere('desc', 'like', "%{$search}%");
            });
        }

        if ($request->filled('type') && $request->type !== 'all') {
            $query->where('type', $request->type);
        }

        $items = $query->limit(500)->get();

        return response()->json($items);
    }

    public function store(Request $request): JsonResponse
    {
        $request->validate([
            'type' => 'required|integer',
            'val'  => 'required|string|max:500',
            'desc' => 'nullable|string|max:1000',
        ]);

        $exists = DB::table('blacklist')
            ->where('type', $request->type)
            ->where('val', $request->val)
            ->exists();

        if ($exists) {
            return response()->json(['message' => 'Bu kayıt zaten mevcut.'], 422);
        }

        DB::table('blacklist')->insert([
            'type' => $request->type,
            'val'  => $request->val,
            'desc' => $request->desc,
        ]);

        return response()->json(['message' => 'Kara listeye eklendi.']);
    }

    public function update(int $id, Request $request): JsonResponse
    {
        $request->validate([
            'desc' => 'nullable|string|max:1000',
        ]);

        $item = DB::table('blacklist')->where('id', $id)->first();
        if (! $item) {
            return response()->json(['message' => 'Kayıt bulunamadı.'], 404);
        }

        DB::table('blacklist')->where('id', $id)->update([
            'desc' => $request->desc,
        ]);

        return response()->json(['message' => 'Güncellendi.']);
    }

    public function destroy(int $id): JsonResponse
    {
        $item = DB::table('blacklist')->where('id', $id)->first();
        if (! $item) {
            return response()->json(['message' => 'Kayıt bulunamadı.'], 404);
        }

        // Aynı type+val'a sahip TÜM duplikate kayıtları sil (örn. aynı player_id birden fazla eklenmişse)
        $deleted = DB::table('blacklist')
            ->where('type', $item->type)
            ->where('val', $item->val)
            ->delete();

        return response()->json([
            'message' => $deleted > 1
                ? "Kara listeden silindi ({$deleted} duplikate kayıt birlikte temizlendi)."
                : 'Kara listeden silindi.',
            'deleted' => $deleted,
        ]);
    }

    public function check(Request $request): JsonResponse
    {
        $request->validate(['val' => 'required|string']);

        $exists = DB::table('blacklist')->where('val', $request->val)->exists();

        return response()->json(['blacklisted' => $exists]);
    }

    /**
     * Blacklist'i Excel (.xlsx) olarak indir.
     * Aynı filtreler (search, type) uygulanır; LIMIT yok — tüm kayıtlar.
     */
    public function export(Request $request): StreamedResponse
    {
        $query = DB::table('blacklist')->orderByDesc('id');

        if ($request->filled('search')) {
            $search = $request->search;
            $query->where(function ($q) use ($search) {
                $q->where('val', 'like', "%{$search}%")
                  ->orWhere('desc', 'like', "%{$search}%");
            });
        }
        if ($request->filled('type') && $request->type !== 'all') {
            $query->where('type', $request->type);
        }

        $items = $query->get();

        $typeLabels = [1 => 'Oyuncu', 2 => 'IBAN', 3 => 'IP', 4 => 'E-posta'];

        $spreadsheet = new Spreadsheet();
        $sheet = $spreadsheet->getActiveSheet();
        $sheet->setTitle('Kara Liste');

        // Header
        $headers = ['#', 'Tip', 'Değer', 'Açıklama'];
        $sheet->fromArray($headers, null, 'A1');

        // Header style
        $headerRange = 'A1:D1';
        $sheet->getStyle($headerRange)->getFont()->setBold(true)->setSize(11);
        $sheet->getStyle($headerRange)->getFill()
            ->setFillType(Fill::FILL_SOLID)
            ->getStartColor()->setRGB('E53935');
        $sheet->getStyle($headerRange)->getFont()->getColor()->setRGB('FFFFFF');
        $sheet->getStyle($headerRange)->getAlignment()->setHorizontal(Alignment::HORIZONTAL_CENTER);

        // Data rows
        $row = 2;
        foreach ($items as $item) {
            $sheet->setCellValue('A' . $row, (int) $item->id);
            $sheet->setCellValue('B' . $row, $typeLabels[$item->type] ?? ('Tip ' . $item->type));
            $sheet->setCellValueExplicit('C' . $row, (string) $item->val, \PhpOffice\PhpSpreadsheet\Cell\DataType::TYPE_STRING);
            $sheet->setCellValue('D' . $row, (string) ($item->desc ?? ''));
            $row++;
        }

        // Column widths
        $sheet->getColumnDimension('A')->setWidth(8);
        $sheet->getColumnDimension('B')->setWidth(14);
        $sheet->getColumnDimension('C')->setWidth(32);
        $sheet->getColumnDimension('D')->setWidth(60);

        // Border on all data
        $lastRow = max(1, $row - 1);
        $sheet->getStyle('A1:D' . $lastRow)->getBorders()->getAllBorders()->setBorderStyle(Border::BORDER_THIN);

        // Freeze top row
        $sheet->freezePane('A2');

        $filename = 'blacklist_' . now()->format('Y-m-d_His') . '.xlsx';

        return response()->stream(function () use ($spreadsheet) {
            $writer = new Xlsx($spreadsheet);
            $writer->save('php://output');
        }, 200, [
            'Content-Type'        => 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'Content-Disposition' => 'attachment; filename="' . $filename . '"',
            'Cache-Control'       => 'max-age=0',
        ]);
    }
}
