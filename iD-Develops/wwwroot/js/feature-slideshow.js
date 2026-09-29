(function () {
  const selector = "[data-feature-slideshow]";
  const reducedMotionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
  const mobileQuery = window.matchMedia("(max-width: 63.999rem)");

  function toArray(nodeList) {
    return Array.prototype.slice.call(nodeList);
  }

  function initSlideshow(section) {
    const interval = Number(section.dataset.featureInterval) || 5000;
    const rail = section.querySelector(".feature-slideshow-rail");
    const triggers = toArray(section.querySelectorAll("[data-feature-trigger]"));
    const images = toArray(section.querySelectorAll("[data-feature-image]"));
    const descriptions = toArray(section.querySelectorAll("[data-feature-description]"));

    if (triggers.length === 0) {
      return;
    }

    let activeIndex = 0;
    let timeoutId = null;
    let remaining = interval;
    let startedAt = 0;
    let isPaused = false;

    function isReducedMotion() {
      return reducedMotionQuery.matches;
    }

    function setImageFallback(image) {
      const fallback = image.dataset.featureFallback;

      if (!fallback || image.dataset.fallbackApplied === "true") {
        return;
      }

      image.dataset.fallbackApplied = "true";
      image.src = fallback;
    }

    images.forEach(function (image) {
      image.addEventListener("error", function () {
        setImageFallback(image);
      });

      if (image.complete && image.naturalWidth === 0) {
        setImageFallback(image);
      }
    });

    function clearTimer() {
      if (timeoutId) {
        window.clearTimeout(timeoutId);
        timeoutId = null;
      }
    }

    function scheduleTimer(duration) {
      clearTimer();

      if (isPaused || isReducedMotion() || triggers.length < 2) {
        return;
      }

      remaining = duration || interval;
      startedAt = window.performance.now();
      timeoutId = window.setTimeout(function () {
        setActive((activeIndex + 1) % triggers.length);
      }, remaining);
    }

    function resetProgress(trigger) {
      const progress = trigger.querySelector("[data-feature-progress]");

      if (!progress) {
        return;
      }

      progress.style.animation = "none";
      progress.offsetHeight;
      progress.style.animation = "";
    }

    function centerActiveTrigger(trigger) {
      if (!trigger || !rail || !mobileQuery.matches) {
        return;
      }

      window.requestAnimationFrame(function () {
        const railRect = rail.getBoundingClientRect();
        const triggerRect = trigger.getBoundingClientRect();
        const targetLeft = rail.scrollLeft + triggerRect.left - railRect.left - ((rail.clientWidth - triggerRect.width) / 2);

        rail.scrollTo({
          left: targetLeft,
          behavior: isReducedMotion() ? "auto" : "smooth"
        });
      });
    }

    function setActive(nextIndex) {
      activeIndex = (nextIndex + triggers.length) % triggers.length;
      let activeTrigger = null;

      triggers.forEach(function (trigger, index) {
        const isActive = index === activeIndex;

        trigger.dataset.active = isActive ? "true" : "false";
        trigger.setAttribute("aria-selected", isActive ? "true" : "false");

        if (isActive) {
          activeTrigger = trigger;
          resetProgress(trigger);
        }
      });

      descriptions.forEach(function (description, index) {
        description.setAttribute("aria-hidden", index === activeIndex ? "false" : "true");
      });

      images.forEach(function (image, index) {
        const isActive = index === activeIndex;

        image.dataset.active = isActive ? "true" : "false";
        image.setAttribute("aria-hidden", isActive ? "false" : "true");
      });

      remaining = interval;
      centerActiveTrigger(activeTrigger);
      scheduleTimer(interval);
    }

    function pause() {
      if (isPaused) {
        return;
      }

      isPaused = true;
      section.dataset.paused = "true";

      if (timeoutId) {
        remaining = Math.max(0, remaining - (window.performance.now() - startedAt));
      }

      clearTimer();
    }

    function resume() {
      if (!isPaused) {
        return;
      }

      isPaused = false;
      section.dataset.paused = "false";
      scheduleTimer(remaining || interval);
    }

    triggers.forEach(function (trigger, index) {
      trigger.addEventListener("click", function () {
        isPaused = false;
        section.dataset.paused = "false";
        setActive(index);
      });
    });

    section.addEventListener("mouseenter", pause);
    section.addEventListener("mouseleave", resume);

    reducedMotionQuery.addEventListener("change", function () {
      if (isReducedMotion()) {
        clearTimer();
      } else {
        scheduleTimer(interval);
      }
    });

    mobileQuery.addEventListener("change", function () {
      if (mobileQuery.matches) {
        centerActiveTrigger(triggers[activeIndex]);
      }
    });

    setActive(0);
  }

  function initAll() {
    toArray(document.querySelectorAll(selector)).forEach(initSlideshow);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initAll);
  } else {
    initAll();
  }
})();
