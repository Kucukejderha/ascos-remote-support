<?php
/**
 * rotaniz.com ana sayfa RotaLink indirme düğmesi.
 * İndirme dosyası GitHub Releases üzerinden dağıtılır:
 * https://github.com/Kucukejderha/ascos-rotalink/releases/latest/download/RotaLink.exe
 * Canlı tema: /wp-content/themes/rota-theme/header.php (nav CTA)
 */
?>
<a class="btn btn-primary"
   href="<?php echo esc_url('https://github.com/Kucukejderha/ascos-rotalink/releases/latest/download/RotaLink.exe'); ?>"
   download
   aria-label="RotaLink uzaktan destek uygulamasını indir">
    RotaLink indir <span>&darr;</span>
</a>
