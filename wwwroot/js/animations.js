(function jacredGsap() {
  'use strict';

  /* Skip all motion when user prefers reduced motion (a11y) */
  var reduced =
    typeof window !== 'undefined' &&
    window.matchMedia &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function hasGsap() {
    return typeof window.gsap !== 'undefined';
  }

  function initNavbar() {
    if (reduced || !hasGsap()) return;
    var nav = document.querySelector('.jr-navbar');
    if (!nav) return;
    window.gsap.from(nav, { y: -14, opacity: 0, duration: 0.5, ease: 'power3.out' });
  }

  function initHero() {
    if (reduced || !hasGsap()) return;
    var hero = document.querySelector('.jr-hero');
    if (!hero) return;
    var gsap = window.gsap;
    var tl = gsap.timeline({ defaults: { ease: 'power3.out' }, delay: 0.06 });
    var glow = hero.querySelector('.jr-hero-glow');
    var title = hero.querySelector('.jr-hero-title');
    var subs = hero.querySelectorAll('.jr-hero-sub');
    if (glow) tl.from(glow, { scale: 0.6, opacity: 0, duration: 0.65 }, 0);
    if (title) tl.from(title, { y: 24, opacity: 0, duration: 0.6 }, 0.05);
    if (subs.length) tl.from(subs, { y: 12, opacity: 0, duration: 0.4, stagger: 0.06 }, '-=0.35');
  }

  function initPageBlocks() {
    if (reduced || !hasGsap()) return;
    var gsap = window.gsap;
    var blocks = gsap.utils.toArray('.gsap-rise:not([data-jr-dynamic])');
    if (blocks.length) {
      gsap.from(blocks, {
        y: 20,
        opacity: 0,
        duration: 0.5,
        stagger: 0.07,
        ease: 'power2.out',
        delay: 0.12,
        clearProps: 'opacity,transform',
      });
    }
  }

  function initResultCards(container, onlyNodes) {
    if (!container) return;
    var cards;
    if (onlyNodes && onlyNodes.length) {
      cards = onlyNodes;
    } else {
      cards = container.querySelectorAll('.result-card:not([data-jr-animated])');
    }
    if (!cards.length) return;

    Array.prototype.forEach.call(cards, function (card) {
      card.setAttribute('data-jr-animated', '1');
    });

    if (reduced || !hasGsap()) return;

    window.gsap.from(cards, {
      y: 10,
      opacity: 0,
      duration: 0.32,
      stagger: 0.025,
      ease: 'power2.out',
      clearProps: 'opacity,transform',
      overwrite: 'auto',
    });
  }

  function initScrollReveals() {
    if (reduced || !hasGsap()) return;
    var ScrollTrigger = window.ScrollTrigger;
    if (!ScrollTrigger) return;
    window.gsap.registerPlugin(ScrollTrigger);
    document.querySelectorAll('.gsap-scroll').forEach(function (el) {
      window.gsap.from(el, {
        scrollTrigger: { trigger: el, start: 'top 92%', toggleActions: 'play none none none' },
        y: 28,
        opacity: 0,
        duration: 0.55,
        ease: 'power3.out',
        clearProps: 'opacity,transform',
      });
    });
  }

  window.jacredAnimateResults = initResultCards;

  function initStatCards(container, onlyNodes) {
    if (!container) return;
    var cards;
    if (onlyNodes && onlyNodes.length) {
      cards = onlyNodes;
    } else {
      cards = container.querySelectorAll('.stat-card:not([data-jr-animated]), tbody tr:not([data-jr-animated])');
    }
    if (!cards.length) return;

    Array.prototype.forEach.call(cards, function (card) {
      card.setAttribute('data-jr-animated', '1');
    });

    if (reduced || !hasGsap()) return;

    window.gsap.from(cards, {
      y: 10,
      opacity: 0,
      duration: 0.32,
      stagger: 0.02,
      ease: 'power2.out',
      clearProps: 'opacity,transform',
      overwrite: 'auto',
    });
  }

  window.jacredAnimateStatCards = initStatCards;

  function shouldSkipEntrance() {
    try {
      if (sessionStorage.getItem('jr-animated')) return true;
      sessionStorage.setItem('jr-animated', '1');
    } catch (_) {}
    return false;
  }

  function boot() {
    if (shouldSkipEntrance()) return;
    initNavbar();
    initHero();
    initPageBlocks();
    initScrollReveals();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
})();
