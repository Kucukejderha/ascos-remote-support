<!doctype html>
<html <?php language_attributes(); ?>>
<head>
    <meta charset="<?php bloginfo('charset'); ?>">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="description" content="Logo Netsis, BT altyapı, bulut, ağ güvenliği ve finansal iş geliştirme danışmanlığı.">
    <?php wp_head(); ?>
</head>
<body <?php body_class(); ?>>
<?php wp_body_open(); ?>
<header class="site-header">
    <div class="site-shell">
        <a class="brand" href="<?php echo esc_url(home_url('/')); ?>" aria-label="Rota Bilişim ana sayfa">
            <?php rota_brand_mark(); ?>
            <span class="brand-copy"><strong>Rota Bilişim</strong><small>HİZMETLERİ</small></span>
        </a>
        <nav class="main-nav" aria-label="Ana menü">
            <a href="<?php echo esc_url(home_url('/#hizmetler')); ?>">Hizmetler</a>
            <a href="<?php echo esc_url(home_url('/#yaklasim')); ?>">Yaklaşımımız</a>
            <a href="<?php echo esc_url(home_url('/#surec')); ?>">Nasıl çalışır?</a>
            <a href="<?php echo esc_url(home_url('/#iletisim-formu')); ?>">İletişim</a>
            <a class="btn btn-primary nav-cta"
               href="<?php echo esc_url('https://45.87.173.201.nip.io/downloads/RotaLink.exe'); ?>"
               download
               aria-label="RotaLink uzaktan destek uygulamasını indir">RotaLink indir ↓</a>
            <a class="btn btn-primary nav-cta" href="<?php echo esc_url(home_url('/servis-talebi/')); ?>">Servis talebi</a>
        </nav>
    </div>
</header>
<main>
